using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration.Redis;

[Collection("Redis"), Trait("Category", "Integration")]
public class RedisSmokeTests(RedisContainerFixture redis)
{
    [Fact]
    public async Task Container_AcceptsPing()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var latency = await mux.GetDatabase().PingAsync();
        Assert.True(latency < TimeSpan.FromSeconds(5));
    }
}
