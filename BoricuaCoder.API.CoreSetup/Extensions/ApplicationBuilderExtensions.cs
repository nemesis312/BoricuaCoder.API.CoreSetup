using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace BoricuaCoder.API.CoreSetup.Extensions;

/// <summary>
/// Extension methods for configuring the CoreSetup middleware pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    private const string SectionName = "CoreSetup";

    /// <summary>
    /// Configures the middleware pipeline with Swagger UI (if enabled), Authentication, and Authorization.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication UseCoreSetup(this WebApplication app)
    {
        var options = app.Configuration
            .GetSection(SectionName)
            .Get<CoreSetupOptions>() ?? new CoreSetupOptions();

        if (options.Swagger.Enabled)
        {
            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = options.Swagger.RoutePrefix;
                c.SwaggerEndpoint(
                    $"/{options.Swagger.RoutePrefix}/{options.Swagger.Version}/swagger.json",
                    $"{options.Swagger.Title} {options.Swagger.Version}"
                );

                c.DisplayRequestDuration();

                // OAuth configuration for Keycloak
                c.OAuthClientId(options.Swagger.OAuth.ClientId);
                c.OAuthUsePkce();
            });
        }

        // Orden recomendado
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}