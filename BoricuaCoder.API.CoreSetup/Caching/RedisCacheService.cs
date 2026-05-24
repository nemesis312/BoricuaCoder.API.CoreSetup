using System.Text.Json;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Caching;

internal sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisOptions _options;

    public RedisCacheService(
        IDistributedCache cache,
        IConnectionMultiplexer multiplexer,
        RedisOptions options)
    {
        _cache = cache;
        _multiplexer = multiplexer;
        _options = options;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var redisTask = _cache.GetStringAsync($"{_options.PrefixKey}{key}", ct);
            if (!await WaitWithShortCircuit(redisTask))
                return default;

            var json = redisTask.Result;
            return json is null ? default : JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            var entryOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            };
            var redisTask = _cache.SetStringAsync($"{_options.PrefixKey}{key}", json, entryOptions, ct);
            await WaitWithShortCircuit(redisTask);
        }
        catch { /* swallow — cache failures must not break the app */ }
    }

    public async Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken ct = default)
    {
        try
        {
            var server = GetServer();
            var fullPattern = $"{_options.PrefixKey}{pattern}";
            var db = _multiplexer.GetDatabase();
            var keys = new List<string>();

            await foreach (var key in server.KeysAsync(db.Database, fullPattern).WithCancellation(ct))
                keys.Add(key.ToString());

            return keys;
        }
        catch
        {
            return [];
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync($"{_options.PrefixKey}{key}", ct);
        }
        catch { }
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        try
        {
            var server = GetServer();
            var db = _multiplexer.GetDatabase();

            await foreach (var key in server.KeysAsync(db.Database, $"{_options.PrefixKey}*").WithCancellation(ct))
                await db.KeyDeleteAsync(key);
        }
        catch { }
    }

    public async Task DeleteCascadeAsync(string keySegment, CancellationToken ct = default)
    {
        try
        {
            var server = GetServer();
            var db = _multiplexer.GetDatabase();
            var exactKey = (RedisKey)$"{_options.PrefixKey}{keySegment}";
            var cascadePattern = $"{_options.PrefixKey}{keySegment}:*";

            await db.KeyDeleteAsync(exactKey);

            await foreach (var key in server.KeysAsync(db.Database, cascadePattern).WithCancellation(ct))
                await db.KeyDeleteAsync(key);
        }
        catch { }
    }

    private IServer GetServer()
    {
        var endpoints = _multiplexer.GetEndPoints();
        return _multiplexer.GetServer(endpoints[0]);
    }

    /// <summary>
    /// Returns true if the task completed before the short-circuit timeout.
    /// When short-circuit is disabled (0), always awaits and returns true.
    /// </summary>
    private async Task<bool> WaitWithShortCircuit(Task task)
    {
        if (_options.ShortCircuit <= 0)
        {
            await task;
            return true;
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(_options.ShortCircuit));
        var winner = await Task.WhenAny(task, timeout);
        return winner != timeout;
    }
}
