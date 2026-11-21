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

                // Mutate the existing parameters: replacing the instance would drop defaults and
                // anything configured by other IConfigureOptions<JwtBearerOptions>.
                var tv = options.TokenValidation;
                var parameters = jwt.TokenValidationParameters;

                if (tv.ValidateIssuer.HasValue)
                    parameters.ValidateIssuer = tv.ValidateIssuer.Value;
                if (tv.ValidateAudience.HasValue)
                    parameters.ValidateAudience = tv.ValidateAudience.Value;
                if (tv.ValidateLifetime.HasValue)
                    parameters.ValidateLifetime = tv.ValidateLifetime.Value;
                if (tv.ClockSkewSeconds.HasValue)
                    parameters.ClockSkew = TimeSpan.FromSeconds(tv.ClockSkewSeconds.Value);

                if (options.ValidAlgorithms.Length > 0)
                    parameters.ValidAlgorithms = options.ValidAlgorithms;
            });

        services.AddAuthorization();

        return services;
    }
}