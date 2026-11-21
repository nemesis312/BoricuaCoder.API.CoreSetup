using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Extensions;
using BoricuaCoder.API.CoreSetup.Options;
using BoricuaCoder.API.CoreSetup.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Tests.Extensions;

public class RedisSetupTests
{
    [Theory]
    [InlineData("redis.example.com:6379", true)]
    [InlineData("redis.example.com:6379,ssl=false", true)]
    [InlineData("10.0.0.5:6379", true)]
    [InlineData("localhost:6379,redis.example.com:6379", true)]
    [InlineData("redis.example.com:6380,ssl=true", false)]
    [InlineData("localhost:6379", false)]
    [InlineData("LOCALHOST:6379", false)]
    [InlineData("127.0.0.1:6379", false)]
    [InlineData("[::1]:6379", false)]
    [InlineData("localhost:6379,127.0.0.1:6380", false)]
    public void IsInsecureRemote_ClassifiesEndpoints(string connectionString, bool expected)
    {
        Assert.Equal(expected, RedisSetup.IsInsecureRemote(connectionString));
    }

    [Fact]
    public void AddCoreSetup_RedisEnabled_DoesNotConnectDuringRegistration()
    {
        var services = new ServiceCollection();
        var configuration = Config("unreachable.invalid:6379");

        var ex = Record.Exception(() => services.AddCoreSetup(configuration));

        Assert.Null(ex);
        Assert.Contains(services, d => d.IsKeyedService
            && Equals(d.ServiceKey, RedisSetup.MultiplexerServiceKey)
            && d.ServiceType == typeof(IConnectionMultiplexer)
            && d.KeyedImplementationFactory is not null);
        Assert.Contains(services, d => d.ServiceType == typeof(ICacheService));
    }

    [Fact]
    public void AddCoreSetup_RedisEnabledWithEmptyConnectionString_SkipsRegistrationAndValidatorReportsError()
    {
        var services = new ServiceCollection();
        var configuration = Config("");

        services.AddCoreSetup(configuration);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ICacheService));
        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<CoreSetupOptions>>().Value);
        Assert.Contains("CoreSetup:Redis:ConnectionString is required when Redis is enabled.", ex.Failures);
    }

    private static IConfiguration Config(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CoreSetup:Redis:Enabled"] = "true",
                ["CoreSetup:Redis:ConnectionString"] = connectionString,
                ["CoreSetup:Redis:PrefixKey"] = "app::"
            })
            .Build();
}
