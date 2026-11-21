using System.Net;
using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Extensions;
using BoricuaCoder.API.CoreSetup.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration.Redis;

[Collection("Redis"), Trait("Category", "Integration")]
public sealed class RedisCachingIntegrationTests(RedisContainerFixture redis)
{
    private readonly string _prefix = $"it::{Guid.NewGuid():N}::";

    [Fact]
    public async Task CacheAttribute_SecondCall_IsServedFromRedis()
    {
        var hits = new HitCounter();
        await using var app = BuildApp(hits, ttlSeconds: 60);
        await app.StartAsync();
        var client = app.GetTestClient();

        var first = await client.GetAsync("/items/42");
        var second = await client.GetAsync("/items/42");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, hits.Value);

        var keys = await ScanAsync($"{CacheGlob(_prefix)}*");
        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.StartsWith(_prefix, k));
    }

    [Fact]
    public async Task CacheAttribute_EntryExpiresAfterTtl()
    {
        var hits = new HitCounter();
        await using var app = BuildApp(hits, ttlSeconds: 1);
        await app.StartAsync();
        var client = app.GetTestClient();

        (await client.GetAsync("/items/7")).EnsureSuccessStatusCode();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        (await client.GetAsync("/items/7")).EnsureSuccessStatusCode();

        Assert.Equal(2, hits.Value);
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesOnlyPrefixedKeys()
    {
        await using var app = BuildApp(new HitCounter(), ttlSeconds: 60);
        await app.StartAsync();
        var cache = app.Services.GetRequiredService<ICacheService>();
        var foreignKey = $"other:{Guid.NewGuid():N}:x";

        await using var mux = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var db = mux.GetDatabase();
        await db.StringSetAsync(foreignKey, "keep");

        try
        {
            await cache.SetAsync("a", 1, TimeSpan.FromMinutes(1));
            await cache.SetAsync("b:c", 2, TimeSpan.FromMinutes(1));
            Assert.Equal(2, (await ScanAsync($"{CacheGlob(_prefix)}*")).Count);

            await cache.DeleteAllAsync();

            Assert.Empty(await ScanAsync($"{CacheGlob(_prefix)}*"));
            Assert.Equal("keep", (string?)await db.StringGetAsync(foreignKey));
        }
        finally
        {
            await db.KeyDeleteAsync(foreignKey);
        }
    }

    [Fact]
    public async Task DeleteCascadeAsync_GlobSegment_DeletesOnlyLiteral()
    {
        await using var app = BuildApp(new HitCounter(), ttlSeconds: 60);
        await app.StartAsync();
        var cache = app.Services.GetRequiredService<ICacheService>();

        await cache.SetAsync("U:1:a", 1, TimeSpan.FromMinutes(1));
        await cache.SetAsync("U:2:a", 2, TimeSpan.FromMinutes(1));

        await cache.DeleteCascadeAsync("*");

        Assert.Equal(1, await cache.GetAsync<int>("U:1:a"));
        Assert.Equal(2, await cache.GetAsync<int>("U:2:a"));
    }

    [Fact]
    public async Task App_StartsAndServes_WhenRedisUnreachable()
    {
        var hits = new HitCounter();
        await using var app = BuildApp(hits, ttlSeconds: 60, overrides: new()
        {
            ["CoreSetup:Redis:ConnectionString"] =
                "localhost:1,connectTimeout=200,asyncTimeout=500,syncTimeout=500",
            ["CoreSetup:Redis:ShortCircuit"] = "1"
        });

        await app.StartAsync();
        var client = app.GetTestClient();

        var first = await client.GetAsync("/items/1");
        var second = await client.GetAsync("/items/1");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, hits.Value);
    }

    [Fact]
    public async Task ConsumerMultiplexer_RegisteredAfterCoreSetup_IsNotReplacedAndLibraryUsesKeyed()
    {
        var consumer = Substitute.For<IConnectionMultiplexer>();
        var hits = new HitCounter();
        await using var app = BuildApp(hits, ttlSeconds: 60,
            configureServices: s => s.AddSingleton(consumer));
        await app.StartAsync();
        var client = app.GetTestClient();

        (await client.GetAsync("/items/9")).EnsureSuccessStatusCode();
        (await client.GetAsync("/items/9")).EnsureSuccessStatusCode();

        Assert.Same(consumer, app.Services.GetRequiredService<IConnectionMultiplexer>());
        var keyed = app.Services.GetRequiredKeyedService<IConnectionMultiplexer>(RedisSetup.MultiplexerServiceKey);
        Assert.NotSame(consumer, keyed);
        Assert.Equal(1, hits.Value);
        Assert.Empty(consumer.ReceivedCalls());
    }

    [Fact]
    public async Task ConsumerMultiplexer_RegisteredBeforeCoreSetup_IsNotReplaced()
    {
        var consumer = Substitute.For<IConnectionMultiplexer>();
        await using var app = BuildApp(new HitCounter(), ttlSeconds: 60,
            configureServicesBefore: s => s.AddSingleton(consumer));

        Assert.Same(consumer, app.Services.GetRequiredService<IConnectionMultiplexer>());
    }

    [Fact]
    public async Task UnkeyedMultiplexer_WithoutConsumerRegistration_AliasesKeyedInstance()
    {
        await using var app = BuildApp(new HitCounter(), ttlSeconds: 60);

        var unkeyed = app.Services.GetRequiredService<IConnectionMultiplexer>();
        var keyed = app.Services.GetRequiredKeyedService<IConnectionMultiplexer>(RedisSetup.MultiplexerServiceKey);

        Assert.Same(keyed, unkeyed);
        // abortConnect=false returns before the handshake may finish; a ping waits for a live connection.
        await keyed.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(keyed.IsConnected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class HitCounter
    {
        private int _value;
        public int Value => Volatile.Read(ref _value);
        public int Increment() => Interlocked.Increment(ref _value);
    }

    private WebApplication BuildApp(
        HitCounter hits,
        int ttlSeconds,
        Dictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null,
        Action<IServiceCollection>? configureServicesBefore = null)
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Redis:Enabled"] = "true",
            ["CoreSetup:Redis:ConnectionString"] = redis.ConnectionString,
            ["CoreSetup:Redis:PrefixKey"] = _prefix
        };
        foreach (var (key, value) in overrides ?? [])
            config[key] = value;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(config);
        configureServicesBefore?.Invoke(builder.Services);
        builder.Services.AddCoreSetup(builder.Configuration);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseCoreSetup();
        app.MapGet("/items/{id}", (int id) =>
            {
                var hit = hits.Increment();
                return Results.Ok(new { id, hit });
            })
            .AddEndpointFilter(new CacheAttribute(ttlSeconds));

        return app;
    }

    private static string CacheGlob(string value) => RedisCacheService.EscapeGlob(value);

    private async Task<List<string>> ScanAsync(string pattern)
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var server = mux.GetServers().Single();
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: pattern))
            keys.Add(key.ToString());
        return keys;
    }
}
