using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoricuaCoder.API.CoreSetup.Extensions;

/// <summary>
/// Extension methods for configuring the CoreSetup middleware pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    private const string SectionName = "CoreSetup";
    internal const string LogCategory = "BoricuaCoder.API.CoreSetup";

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

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LogCategory);

        LogJwtConfiguration(logger, options.Jwt);

        if (options.Swagger.Enabled)
        {
            LogSwaggerConfiguration(logger, options.Swagger);

            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = options.Swagger.RoutePrefix;
                c.SwaggerEndpoint(
                    $"/{options.Swagger.RoutePrefix}/{options.Swagger.Version}/swagger.json",
                    $"{options.Swagger.Title} {options.Swagger.Version}"
                );

                c.DisplayRequestDuration();
                c.OAuthClientId(options.Swagger.OAuth.ClientId);
                c.OAuthUsePkce();
            });
        }
        else
        {
            logger.LogDebug("CoreSetup: Swagger UI is disabled.");
        }

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    private static void LogJwtConfiguration(ILogger logger, JwtOptions jwt)
    {
        var authority = string.IsNullOrEmpty(jwt.Authority) ? "(not set)" : jwt.Authority;

        logger.LogDebug(
            "CoreSetup: JWT authentication configured. Authority={Authority}, Audience={Audience}",
            authority, string.IsNullOrEmpty(jwt.Audience) ? "(not set)" : jwt.Audience);

        if (!jwt.RequireHttpsMetadata)
            logger.LogWarning(
                "CoreSetup: RequireHttpsMetadata is disabled for Authority={Authority}. Do not use this setting in production.",
                authority);
    }

    private static void LogSwaggerConfiguration(ILogger logger, SwaggerOptions swagger)
    {
        logger.LogDebug(
            "CoreSetup: Swagger UI enabled. Title={Title}, Version={Version}, RoutePrefix={RoutePrefix}",
            swagger.Title, swagger.Version, swagger.RoutePrefix);

        var hasOAuth = !string.IsNullOrEmpty(swagger.OAuth.AuthorizationUrl);
        if (hasOAuth)
            logger.LogDebug(
                "CoreSetup: Swagger OAuth2 configured. ClientId={ClientId}",
                swagger.OAuth.ClientId);
    }
}