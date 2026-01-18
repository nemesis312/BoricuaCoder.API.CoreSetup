using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.DependencyInjection;

namespace BoricuaCoder.API.CoreSetup.Swagger;

internal static class SwaggerSetup
{
    internal static IServiceCollection AddSwagger(
        this IServiceCollection services,
        SwaggerOptions options)
    {
        if (!options.Enabled) return services;

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(options.Version, new()
            {
                Title = options.Title,
                Version = options.Version
            });

            c.AddSecurityDefinition("Bearer", new()
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header
            });

            c.AddSecurityRequirement(new()
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}