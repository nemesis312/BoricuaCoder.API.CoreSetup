using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace BoricuaCoder.API.CoreSetup.Setup;

internal static class JwtAuthenticationSetup
{
    internal static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        JwtOptions options)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
            });

        services.AddAuthorization();

        return services;
    }
}