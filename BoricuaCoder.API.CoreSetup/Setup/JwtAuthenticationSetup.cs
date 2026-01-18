using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.DependencyInjection;

namespace BoricuaCoder.API.CoreSetup.Authentication;

internal static class JwtAuthenticationSetup
{
    internal static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        JwtOptions options)
    {
        services
            .AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", o =>
            {
                o.Authority = options.Authority;
                o.Audience  = options.Audience;
                o.RequireHttpsMetadata = options.RequireHttpsMetadata;
            });

        return services;
    }
}