using System.Text;
using System.Text.Json;
using BoricuaCoder.API.CoreSetup.Options;
using BoricuaCoder.API.CoreSetup.Setup;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Caching;

internal sealed class RedisCacheService : ICacheService
{
    private const int DeleteBatchSize = 500;

    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger _logger;

    public RedisCacheService(
        IDistributedCache cache,
        [FromKeyedServices(RedisSetup.MultiplexerServiceKey)] IConnectionMultiplexer multiplexer,
        RedisOptions options,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>? jsonOptions = null,
        ILogger<RedisCacheService>? logger = null)
    {
        _cache = cache;
        _multiplexer = multiplexer;
        _options = options;
        _serializerOptions = jsonOptions?.Value?.SerializerOptions ?? JsonSerializerOptions.Default;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var redisTask = _cache.GetStringAsync($"{_options.PrefixKey}{key}", ct);
            if (!await WaitWithShortCircuit(redisTask, nameof(GetAsync), key, ct))
                return default;

            var json = await redisTask;
            return json is null ? default : JsonSerializer.Deserialize<T>(json, _serializerOptions);
        }
        catch (Exception ex)
        {
            LogFailure(ex, nameof(GetAsync), key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _serializerOptions);
            var entryOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            };
            var redisTask = _cache.SetStringAsync($"{_options.PrefixKey}{key}", json, entryOptions, ct);
            await WaitWithShortCircuit(redisTask, nameof(SetAsync), key, ct);
        }
        catch (Exception ex)
        {
            // Cache failures must not break the app.
            LogFailure(ex, nameof(SetAsync), key);
        }
    }

    /// <summary>
    /// Returns every key under the prefix that matches <paramref name="pattern"/>.
    /// The prefix is glob-escaped; <paramref name="pattern"/> is passed to Redis SCAN as a glob,
    /// so it must be a developer-defined constant and must not contain user input.
    /// </summary>
    public async Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken ct = default)
    {
        try
        {
            var fullPattern = EscapeGlob(_options.PrefixKey) + pattern;
            var keys = await ScanAsync(fullPattern, ct);
            return keys;
        }
        catch (Exception ex)
        {
            LogFailure(ex, nameof(GetKeysAsync), pattern);
            return [];
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync($"{_options.PrefixKey}{key}", ct);
        }
        catch (Exception ex)
        {
            LogFailure(ex, nameof(DeleteAsync), key);
        }
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.PrefixKey))
        {
            _logger.LogWarning(
                "CoreSetup: DeleteAllAsync skipped because CoreSetup:Redis:PrefixKey is empty; refusing to delete every key in the Redis database.");
            return;
        }

        try
        {
            var keys = await ScanAsync(EscapeGlob(_options.PrefixKey) + "*", ct);
            var deleted = await DeleteInBatchesAsync(keys, ct);

            _logger.LogInformation(
                "CoreSetup: DeleteAllAsync removed {Count} cache keys under the configured prefix.", deleted);
        }
        catch (Exception ex)
        {
            LogFailure(ex, nameof(DeleteAllAsync), "*");
        }
    }

    public async Task DeleteCascadeAsync(string keySegment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keySegment))
        {
            _logger.LogWarning(
                "CoreSetup: DeleteCascadeAsync skipped because the key segment is empty; refusing to delete every key under the prefix.");
            return;
        }

        try
        {
            var db = _multiplexer.GetDatabase();
            await db.KeyDeleteAsync((RedisKey)$"{_options.PrefixKey}{keySegment}");

            var keys = await ScanAsync(EscapeGlob(_options.PrefixKey + keySegment) + ":*", ct);
            await DeleteInBatchesAsync(keys, ct);
        }
        catch (Exception ex)
        {
            LogFailure(ex, nameof(DeleteCascadeAsync), keySegment);
        }
    }

    /// <summary>
    /// Escapes the Redis glob metacharacters <c>\ * ? [ ]</c> with a backslash so
    /// <paramref name="value"/> matches literally in SCAN/KEYS patterns.
    /// </summary>
    internal static string EscapeGlob(string value)
    {
        if (value.AsSpan().IndexOfAny(@"\*?[]") < 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (c is '\\' or '*' or '?' or '[' or ']')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// SCANs every connected primary (replicas hold copies of the same keys) and dedupes the result.
    /// </summary>
    private async Task<HashSet<string>> ScanAsync(string pattern, CancellationToken ct)
    {
        var database = _multiplexer.GetDatabase().Database;
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var server in _multiplexer.GetServers().Where(s => s.IsConnected && !s.IsReplica))
        {
            await foreach (var key in server.KeysAsync(database, pattern).WithCancellation(ct))
                keys.Add(key.ToString());
        }

        return keys;
    }

    private async Task<long> DeleteInBatchesAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
            return 0;

        var db = _multiplexer.GetDatabase();
        long deleted = 0;

        foreach (var chunk in keys.Chunk(DeleteBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            var batch = Array.ConvertAll(chunk, k => (RedisKey)k);
            deleted += await db.KeyDeleteAsync(batch);
        }

        return deleted;
    }

    private void LogFailure(Exception ex, string operation, string key) =>
        _logger.LogWarning(ex,
            "CoreSetup: Redis cache {Operation} failed for key '{Key}'; continuing without the cache.",
            operation, key);

    /// <summary>
    /// Returns true if the task completed before the short-circuit timeout.
    /// When short-circuit is disabled (0), always awaits and returns true.
    /// </summary>
    private async Task<bool> WaitWithShortCircuit(Task task, string operation, string key, CancellationToken ct)
    {
        if (_options.ShortCircuit <= 0)
        {
            await task;
            return true;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(_options.ShortCircuit), ct);
            return true;
        }
        catch (TimeoutException ex)
        {
            // Observe a late fault so it never surfaces as an UnobservedTaskException.
            _ = task.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            _logger.LogWarning(ex,
                "CoreSetup: Redis cache {Operation} for key '{Key}' exceeded the {ShortCircuit}s short-circuit; bypassing the cache.",
                operation, key, _options.ShortCircuit);
            return false;
        }
    }
}
