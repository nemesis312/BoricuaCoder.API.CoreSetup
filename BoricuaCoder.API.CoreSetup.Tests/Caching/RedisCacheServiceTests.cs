using System.Net;
using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
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

    public RedisCacheServiceTests()
    {
        _distributedCache = new MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

        _multiplexer = Substitute.For<IConnectionMultiplexer>();
        _server = Substitute.For<IServer>();
        _database = Substitute.For<IDatabase>();

        _multiplexer.GetEndPoints().Returns([new IPEndPoint(IPAddress.Loopback, 6379)]);
        _multiplexer.GetServer(Arg.Any<EndPoint>(), Arg.Any<object?>()).Returns(_server);
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);
    }

    private RedisCacheService CreateService(RedisOptions? options = null)
    {
        options ??= new RedisOptions { PrefixKey = "test::", DefaultTTL = 300 };
        return new RedisCacheService(_distributedCache, _multiplexer, options);
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
        var service = new RedisCacheService(_distributedCache, _multiplexer, options);
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
        _multiplexer.GetEndPoints().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService();

        var keys = await service.GetKeysAsync("*");

        Assert.Empty(keys);
    }

    // ── DeleteAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_DeletesEveryKeyUnderPrefix()
    {
        RedisKey[] stored = ["test::key1", "test::key2", "test::key3"];
        _server.KeysAsync(Arg.Any<int>(), Arg.Any<RedisValue>(),
                Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(AsyncKeys(stored));
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);
        var service = CreateService();

        await service.DeleteAllAsync();

        await _database.Received(3).KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DeleteAllAsync_WhenServerThrows_DoesNotThrow()
    {
        _multiplexer.GetEndPoints().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() => service.DeleteAllAsync());

        Assert.Null(exception);
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
        var service = CreateService();

        await service.DeleteCascadeAsync("UserInfo:42");

        // 1 call for the exact key + 2 calls for the children
        await _database.Received(3).KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DeleteCascadeAsync_WhenServerThrows_DoesNotThrow()
    {
        _multiplexer.GetEndPoints().Returns(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var service = CreateService();

        var exception = await Record.ExceptionAsync(
            () => service.DeleteCascadeAsync("UserInfo:42"));

        Assert.Null(exception);
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
