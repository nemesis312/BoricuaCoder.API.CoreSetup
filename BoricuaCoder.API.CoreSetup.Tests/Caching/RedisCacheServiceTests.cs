using System.Diagnostics;
using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
using BoricuaCoder.API.CoreSetup.Tests.Integration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Tests.Caching;

public class RedisCacheServiceTests
{
    private readonly IDistributedCache _distributedCache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IServer _server;
    private readonly IDatabase _database;
    private readonly TestLogCollector _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public RedisCacheServiceTests()
    {
        _distributedCache = new MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _server = Substitute.For<IServer>();
        _database = Substitute.For<IDatabase>();

        _server.IsConnected.Returns(true);
        _server.IsReplica.Returns(false);
        _multiplexer.GetServers().Returns([_server]);
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);

        _loggerFactory = LoggerFactory.Create(b => b.AddProvider(new TestLoggerProvider(_logs)));
    }

    private RedisCacheService CreateService(RedisOptions? options = null, IDistributedCache? cache = null)
    {
        options ??= new RedisOptions { PrefixKey = "test::", DefaultTTL = 300 };
        return new RedisCacheService(
            cache ?? _distributedCache,
            _multiplexer,
            options,
            logger: _loggerFactory.CreateLogger<RedisCacheService>());
    }

    private IReadOnlyList<LogRecord> Warnings() =>
        _logs.GetSnapshot().Where(r => r.Level == LogLevel.Warning).ToList();

    private static IServer CreateServer(bool isReplica, params RedisKey[] keys)
    {
        var server = Substitute.For<IServer>();
        server.IsConnected.Returns(true);
        server.IsReplica.Returns(isReplica);
        server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(_ => AsyncKeys(keys));
        return server;
    }

    // ── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetAsync<string>("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_KeyExists_ReturnsDeserializedValue()
    {
        var service = CreateService();
        await service.SetAsync("mykey", new TestRecord("Widget", 9.99m), TimeSpan.FromMinutes(5));

        var result = await service.GetAsync<TestRecord>("mykey");

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
        Assert.Equal(9.99m, result.Price);
    }

    [Fact]
    public async Task GetAsync_InvalidJson_ReturnsDefault()
    {
        await _distributedCache.SetStringAsync("test::broken", "not-valid-json");
        var service = CreateService();

        var result = await service.GetAsync<TestRecord>("broken");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithShortCircuitEnabled_StillReturnsValueWhenFast()
    {
        var options = new RedisOptions { PrefixKey = "test::", DefaultTTL = 300, ShortCircuit = 30 };
        var service = CreateService(options);
        await service.SetAsync("sckey", "hello", TimeSpan.FromMinutes(1));

        var result = await service.GetAsync<string>("sckey");

        Assert.Equal("hello", result);
    }

    // ── SetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_StoresValue_RetrievableByGet()
    {
        var service = CreateService();

        await service.SetAsync("stored", new TestRecord("Gadget", 49.99m), TimeSpan.FromMinutes(1));
        var result = await service.GetAsync<TestRecord>("stored");

        Assert.NotNull(result);
        Assert.Equal("Gadget", result.Name);
    }

    [Fact]
    public async Task SetAsync_DoesNotThrow_WhenValueIsNull()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(
            () => service.SetAsync<string?>("nullkey", null, TimeSpan.FromMinutes(1)));

        Assert.Null(exception);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesStoredEntry()
    {
        var service = CreateService();
        await service.SetAsync("toDelete", "value", TimeSpan.FromMinutes(1));

        await service.DeleteAsync("toDelete");

        var result = await service.GetAsync<string>("toDelete");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentKey_DoesNotThrow()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(
            () => service.DeleteAsync("ghost"));

        Assert.Null(exception);
    }

    // ── GetKeysAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetKeysAsync_ReturnsKeysMatchingPattern()
    {
        RedisKey[] stored = ["test::UserInfo:1", "test::UserInfo:2"];
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys(stored));
        var service = CreateService();

        var keys = (await service.GetKeysAsync("UserInfo:*")).ToList();

        Assert.Equal(2, keys.Count);
        Assert.Contains("test::UserInfo:1", keys);
        Assert.Contains("test::UserInfo:2", keys);
    }

    [Fact]
    public async Task GetKeysAsync_WhenServerThrows_ReturnsEmpty()
    {
        _multiplexer.GetServers().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService();

        var keys = await service.GetKeysAsync("*");

        Assert.Empty(keys);
        Assert.Contains("GetKeysAsync", Assert.Single(Warnings()).Message);
    }

    [Fact]
    public async Task GetKeysAsync_EscapesPrefixButKeepsCallerGlob()
    {
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys([]));
        var service = CreateService(new RedisOptions { PrefixKey = "p?:", DefaultTTL = 300 });

        await service.GetKeysAsync("UserInfo:*");

        _server.Received(1).KeysAsync(Arg.Any<int>(), (RedisValue)@"p\?:UserInfo:*",
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetKeysAsync_DedupesKeysAcrossPrimaries()
    {
        var serverA = CreateServer(false, "test::a", "test::b");
        var serverB = CreateServer(false, "test::b", "test::c");
        _multiplexer.GetServers().Returns([serverA, serverB]);
        var service = CreateService();

        var keys = (await service.GetKeysAsync("*")).Order().ToList();

        Assert.Equal(["test::a", "test::b", "test::c"], keys);
    }

    // ── DeleteAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_DeletesEveryKeyUnderPrefix()
    {
        RedisKey[] stored = ["test::key1", "test::key2", "test::key3"];
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys(stored));
        _database.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>()).Returns(3L);
        var service = CreateService();

        await service.DeleteAllAsync();

        await _database.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey[]>(k => k.Length == 3), Arg.Any<CommandFlags>());
        _server.Received(1).KeysAsync(Arg.Any<int>(), (RedisValue)"test::*",
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>());
        Assert.Contains(_logs.GetSnapshot(), r => r.Level == LogLevel.Information
            && r.Message.StartsWith("CoreSetup:") && r.Message.Contains('3'));
    }

    [Fact]
    public async Task DeleteAllAsync_EmptyPrefix_DoesNotScanAndLogsWarning()
    {
        var service = CreateService(new RedisOptions { PrefixKey = "", DefaultTTL = 300 });

        await service.DeleteAllAsync();

        _server.DidNotReceiveWithAnyArgs().KeysAsync(default, default, default, default, default, default);
        await _database.DidNotReceiveWithAnyArgs().KeyDeleteAsync(default(RedisKey[])!, default);
        var warning = Assert.Single(Warnings());
        Assert.Equal(
            "CoreSetup: DeleteAllAsync skipped because CoreSetup:Redis:PrefixKey is empty; refusing to delete every key in the Redis database.",
            warning.Message);
    }

    [Fact]
    public async Task DeleteAllAsync_ScansEveryPrimaryAndSkipsReplicas()
    {
        var primaryA = CreateServer(isReplica: false, "test::a1", "test::shared");
        var primaryB = CreateServer(isReplica: false, "test::b1", "test::shared");
        var replica = CreateServer(isReplica: true, "test::a1");
        _multiplexer.GetServers().Returns([primaryA, replica, primaryB]);
        var deleted = new List<RedisKey>();
        _database.KeyDeleteAsync(Arg.Do<RedisKey[]>(deleted.AddRange), Arg.Any<CommandFlags>()).Returns(3L);
        var service = CreateService();

        await service.DeleteAllAsync();

        primaryA.ReceivedWithAnyArgs(1).KeysAsync(default, default, default, default, default, default);
        primaryB.ReceivedWithAnyArgs(1).KeysAsync(default, default, default, default, default, default);
        replica.DidNotReceiveWithAnyArgs().KeysAsync(default, default, default, default, default, default);
        Assert.Equal(["test::a1", "test::b1", "test::shared"], deleted.Select(k => k.ToString()).Order());
    }

    [Fact]
    public async Task DeleteAllAsync_DeletesInBatchesOf500()
    {
        var keys = Enumerable.Range(0, 1201).Select(i => (RedisKey)$"test::k{i}").ToArray();
        var server = CreateServer(false, keys);
        _multiplexer.GetServers().Returns([server]);
        var batchSizes = new List<int>();
        _database.KeyDeleteAsync(Arg.Do<RedisKey[]>(k => batchSizes.Add(k.Length)), Arg.Any<CommandFlags>())
            .Returns(0L);
        var service = CreateService();

        await service.DeleteAllAsync();

        Assert.Equal([500, 500, 201], batchSizes);
    }

    [Fact]
    public async Task DeleteAllAsync_EscapesGlobInPrefix()
    {
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys([]));
        var service = CreateService(new RedisOptions { PrefixKey = "a[1]*:", DefaultTTL = 300 });

        await service.DeleteAllAsync();

        _server.Received(1).KeysAsync(Arg.Any<int>(), (RedisValue)@"a\[1\]\*:*",
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DeleteAllAsync_WhenServerThrows_DoesNotThrow()
    {
        _multiplexer.GetServers().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() => service.DeleteAllAsync());

        Assert.Null(exception);
        var warning = Assert.Single(Warnings());
        Assert.StartsWith("CoreSetup:", warning.Message);
        Assert.Contains("DeleteAllAsync", warning.Message);
    }

    // ── DeleteCascadeAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCascadeAsync_DeletesExactKeyAndChildren()
    {
        RedisKey[] children = ["test::UserInfo:42:permissions", "test::UserInfo:42:roles"];
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys(children));
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);
        _database.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>()).Returns(2L);
        var service = CreateService();

        await service.DeleteCascadeAsync("UserInfo:42");

        // the exact key once, then the children in one batch
        await _database.Received(1).KeyDeleteAsync((RedisKey)"test::UserInfo:42", Arg.Any<CommandFlags>());
        await _database.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey[]>(k => k.Length == 2), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DeleteCascadeAsync_EscapesGlobInSegment()
    {
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys([]));
        var service = CreateService(new RedisOptions { PrefixKey = "app::", DefaultTTL = 300 });

        await service.DeleteCascadeAsync("*");

        _server.Received(1).KeysAsync(Arg.Any<int>(), (RedisValue)"app::\\*:*",
            Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>());
        await _database.Received(1).KeyDeleteAsync((RedisKey)"app::*", Arg.Any<CommandFlags>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteCascadeAsync_EmptySegment_DoesNothing(string segment)
    {
        var service = CreateService();

        await service.DeleteCascadeAsync(segment);

        _multiplexer.DidNotReceive().GetServers();
        _server.DidNotReceiveWithAnyArgs().KeysAsync(default, default, default, default, default, default);
        await _database.DidNotReceiveWithAnyArgs().KeyDeleteAsync(default(RedisKey), default);
        await _database.DidNotReceiveWithAnyArgs().KeyDeleteAsync(default(RedisKey[])!, default);
        var warning = Assert.Single(Warnings());
        Assert.StartsWith("CoreSetup:", warning.Message);
    }

    [Fact]
    public async Task DeleteCascadeAsync_WhenServerThrows_DoesNotThrow()
    {
        _multiplexer.GetServers().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService();

        var exception = await Record.ExceptionAsync(
            () => service.DeleteCascadeAsync("UserInfo:42"));

        Assert.Null(exception);
        var warning = Assert.Single(Warnings());
        Assert.Contains("DeleteCascadeAsync", warning.Message);
        Assert.Contains("UserInfo:42", warning.Message);
    }

    // ── DI ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Constructor_ResolvesKeyedMultiplexer_NotTheHostOne()
    {
        var hostMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_distributedCache);
        services.AddSingleton(new RedisOptions { PrefixKey = "test::" });
        services.AddSingleton(hostMultiplexer);
        services.AddKeyedSingleton(Setup.RedisSetup.MultiplexerServiceKey, _multiplexer);
        services.AddSingleton<ICacheService, RedisCacheService>();
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ICacheService>().GetKeysAsync("*");

        _multiplexer.Received().GetServers();
        hostMultiplexer.DidNotReceive().GetServers();
    }

    // ── EscapeGlob ────────────────────────────────────────────────────────────

    [Fact]
    public void EscapeGlob_EscapesAllMetacharacters() =>
        Assert.Equal(@"a\*b\?c\[d\]e\\f", RedisCacheService.EscapeGlob(@"a*b?c[d]e\f"));

    [Fact]
    public void EscapeGlob_LeavesPlainTextUntouched() =>
        Assert.Equal("app::UserInfo:42", RedisCacheService.EscapeGlob("app::UserInfo:42"));

    // ── Short-circuit and failure logging ─────────────────────────────────────

    [Fact]
    public async Task GetAsync_SlowCache_ReturnsDefaultWithinShortCircuit()
    {
        var never = new TaskCompletionSource<byte[]?>();
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(never.Task);
        var service = CreateService(new RedisOptions { PrefixKey = "test::", ShortCircuit = 1 }, cache);

        var sw = Stopwatch.StartNew();
        var result = await service.GetAsync<string>("slow");
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
        var warning = Assert.Single(Warnings());
        Assert.Contains("GetAsync", warning.Message);
        Assert.Contains("slow", warning.Message);
    }

    [Fact]
    public async Task SetAsync_SlowCache_ReturnsWithinShortCircuit()
    {
        var never = new TaskCompletionSource();
        var cache = Substitute.For<IDistributedCache>();
        cache.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>()).Returns(never.Task);
        var service = CreateService(new RedisOptions { PrefixKey = "test::", ShortCircuit = 1 }, cache);

        var sw = Stopwatch.StartNew();
        var exception = await Record.ExceptionAsync(
            () => service.SetAsync("slow", "v", TimeSpan.FromMinutes(1)));
        sw.Stop();

        Assert.Null(exception);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task SetAsync_CacheThrows_LogsWarningAndDoesNotThrow()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService(cache: cache);

        var exception = await Record.ExceptionAsync(
            () => service.SetAsync("k1", "v", TimeSpan.FromMinutes(1)));

        Assert.Null(exception);
        var warning = Assert.Single(Warnings());
        Assert.StartsWith("CoreSetup:", warning.Message);
        Assert.Contains("SetAsync", warning.Message);
        Assert.Contains("k1", warning.Message);
    }

    [Fact]
    public async Task DeleteAsync_CacheThrows_LogsWarningAndDoesNotThrow()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService(cache: cache);

        var exception = await Record.ExceptionAsync(() => service.DeleteAsync("k2"));

        Assert.Null(exception);
        var warning = Assert.Single(Warnings());
        Assert.StartsWith("CoreSetup:", warning.Message);
        Assert.Contains("DeleteAsync", warning.Message);
        Assert.Contains("k2", warning.Message);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<RedisKey> AsyncKeys(IEnumerable<RedisKey> keys)
    {
        foreach (var key in keys)
            yield return key;
        await Task.CompletedTask;
    }

    private record TestRecord(string Name, decimal Price);
}
