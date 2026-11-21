using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
using BoricuaCoder.API.CoreSetup.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
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
        LogRedisConfiguration(logger, options.Redis);

        if (options.Swagger.Enabled)
        {
            LogSwaggerConfiguration(logger, options.Swagger);

            if (!app.Environment.IsDevelopment())
                logger.LogWarning(
                    "CoreSetup: Swagger UI is enabled in the '{Environment}' environment and exposes the full API surface. It will be disabled by default outside Development in 2.0.0. Set CoreSetup:Swagger:Enabled explicitly.",
                    app.Environment.EnvironmentName);

            var routePrefix = options.Swagger.RoutePrefix;
            var version = options.Swagger.Version;

            app.UseSwagger(c =>
                c.RouteTemplate = BuildSwaggerJsonPath(routePrefix, "{documentName}").TrimStart('/'));

            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = routePrefix;
                c.SwaggerEndpoint(
                    BuildSwaggerJsonPath(routePrefix, version),
                    $"{options.Swagger.Title} {version}"
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

    internal static string BuildSwaggerJsonPath(string routePrefix, string version)
    {
        var prefix = routePrefix?.Trim('/');
        return string.IsNullOrWhiteSpace(prefix)
            ? $"/{version}/swagger.json"
            : $"/{prefix}/{version}/swagger.json";
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

        if (string.IsNullOrEmpty(jwt.Authority))
            logger.LogWarning("CoreSetup: Jwt:Authority is not set; every bearer token will be rejected.");

        if (string.Equals(jwt.Audience, "account", StringComparison.OrdinalIgnoreCase))
            logger.LogWarning(
                "CoreSetup: Jwt:Audience is 'account'. Keycloak adds this audience to tokens of most clients in the realm, so tokens issued for other applications will be accepted. Configure an audience mapper with this API's client id.");

        var tv = jwt.TokenValidation;
        LogDisabledValidation(logger, nameof(tv.ValidateIssuer), tv.ValidateIssuer);
        LogDisabledValidation(logger, nameof(tv.ValidateAudience), tv.ValidateAudience);
        LogDisabledValidation(logger, nameof(tv.ValidateLifetime), tv.ValidateLifetime);
    }

    private static void LogDisabledValidation(ILogger logger, string name, bool? value)
    {
        if (value == false)
            logger.LogWarning(
                "CoreSetup: Jwt:TokenValidation:{Name} is disabled. Do not use this setting in production.", name);
    }

    private static void LogRedisConfiguration(ILogger logger, RedisOptions redis)
    {
        if (!redis.Enabled)
        {
            logger.LogDebug("CoreSetup: Redis caching is disabled.");
            return;
        }

        logger.LogDebug(
            "CoreSetup: Redis caching enabled. PrefixKey={PrefixKey}, DefaultTTL={DefaultTTL}s, ShortCircuit={ShortCircuit}s",
            string.IsNullOrEmpty(redis.PrefixKey) ? "(none)" : redis.PrefixKey,
            redis.DefaultTTL,
            redis.ShortCircuit > 0 ? redis.ShortCircuit.ToString() : "disabled");

        if (string.IsNullOrEmpty(redis.PrefixKey))
            logger.LogWarning(
                "CoreSetup: Redis PrefixKey is empty. Cache keys share the database namespace with other apps and DeleteAllAsync is disabled. Set CoreSetup:Redis:PrefixKey.");

        // Never log the connection string: it can carry a password.
        if (!string.IsNullOrWhiteSpace(redis.ConnectionString) && RedisSetup.IsInsecureRemote(redis.ConnectionString))
            logger.LogWarning(
                "CoreSetup: Redis connection to a non-loopback host does not use TLS (ssl=true). Credentials and cached responses travel in plain text.");
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