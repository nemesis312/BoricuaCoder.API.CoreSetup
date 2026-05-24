using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Setup;

internal static class RedisSetup
{
    internal static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        RedisOptions options)
    {
        var multiplexer = ConnectionMultiplexer.Connect(options.ConnectionString);

        // Register the multiplexer as a singleton so both the distributed cache
        // and RedisCacheService share the same connection pool.
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);

        services.AddStackExchangeRedisCache(o =>
        {
            o.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
        });

        // Make the options available for injection (used by CacheAttribute and RedisCacheService).
        services.AddSingleton(options);
        services.AddSingleton<ICacheService, RedisCacheService>();

        return services;
    }
}
