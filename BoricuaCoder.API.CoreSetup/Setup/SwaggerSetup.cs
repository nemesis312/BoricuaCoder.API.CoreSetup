using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace BoricuaCoder.API.CoreSetup.Setup;

internal static class SwaggerSetup
{
    internal static IServiceCollection AddSwaggerWithOAuth(
        this IServiceCollection services,
        SwaggerOptions options)
    {
        if (!options.Enabled) return services;

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(options.Version, new OpenApiInfo
            {
                Title = options.Title,
                Version = options.Version
            });

            const string schemeName = "OAuth2";

            var scopes = options.OAuth.Scopes.ToDictionary(s => s.Key, s => s.Value);

            var securityScheme = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(options.OAuth.AuthorizationUrl),
                        TokenUrl = new Uri(options.OAuth.TokenUrl),
                        Scopes = scopes
                    }
                },
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = schemeName
                }
            };

            c.AddSecurityDefinition(schemeName, securityScheme);

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { securityScheme, scopes.Keys.ToList() }
            });
        });

        return services;
    }
}