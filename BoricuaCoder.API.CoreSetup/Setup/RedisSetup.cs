using System.Net;
using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Setup;

internal static class RedisSetup
{
    /// <summary>
    /// DI key for the multiplexer CoreSetup owns, so it never collides with one the host app registers.
    /// </summary>
    internal const string MultiplexerServiceKey = "BoricuaCoder.API.CoreSetup.Redis";

    internal static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        RedisOptions options)
    {
        // Lazy: the multiplexer is created on first resolution, never during AddCoreSetup, and
        // AbortOnConnectFail=false lets the app start and serve while Redis is down.
        services.AddKeyedSingleton<IConnectionMultiplexer>(MultiplexerServiceKey, (_, _) =>
        {
            var configuration = ConfigurationOptions.Parse(options.ConnectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });

        // Backward compatibility for consumers that inject IConnectionMultiplexer; their own registration wins.
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            sp.GetRequiredKeyedService<IConnectionMultiplexer>(MultiplexerServiceKey));

        // The distributed cache shares CoreSetup's keyed connection.
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>()
            .Configure<IServiceProvider>((cacheOptions, sp) =>
                cacheOptions.ConnectionMultiplexerFactory = () => Task.FromResult(
                    sp.GetRequiredKeyedService<IConnectionMultiplexer>(MultiplexerServiceKey)));

        // Make the options available for injection (used by CacheAttribute and RedisCacheService).
        services.AddSingleton(options);
        services.AddSingleton<ICacheService, RedisCacheService>();

        return services;
    }

    /// <summary>
    /// True when the connection string does not enable TLS and at least one endpoint is not a loopback host.
    /// A connection string that cannot be parsed returns false; the connection itself reports that error.
    /// </summary>
    internal static bool IsInsecureRemote(string connectionString)
    {
        ConfigurationOptions configuration;
        try
        {
            configuration = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or RedisConnectionException)
        {
            return false;
        }

        return !configuration.Ssl && configuration.EndPoints.Any(endPoint => !IsLoopback(endPoint));
    }

    private static bool IsLoopback(EndPoint endPoint) => endPoint switch
    {
        DnsEndPoint dns => string.Equals(dns.Host, "localhost", StringComparison.OrdinalIgnoreCase),
        IPEndPoint ip => IPAddress.IsLoopback(ip.Address),
        _ => false
    };
}
