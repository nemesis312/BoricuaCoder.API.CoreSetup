using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

                var tv = options.TokenValidation;
                var hasOverrides = tv.ValidateIssuer.HasValue
                    || tv.ValidateAudience.HasValue
                    || tv.ValidateLifetime.HasValue
                    || tv.ClockSkewSeconds.HasValue;

                if (hasOverrides)
                {
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = tv.ValidateIssuer ?? true,
                        ValidateAudience = tv.ValidateAudience ?? true,
                        ValidateLifetime = tv.ValidateLifetime ?? true,
                        ClockSkew = tv.ClockSkewSeconds.HasValue
                            ? TimeSpan.FromSeconds(tv.ClockSkewSeconds.Value)
                            : TimeSpan.FromMinutes(5)
                    };
                }
            });

        services.AddAuthorization();

        return services;
    }
}