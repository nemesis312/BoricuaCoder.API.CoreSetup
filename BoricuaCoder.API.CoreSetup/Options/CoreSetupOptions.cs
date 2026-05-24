namespace BoricuaCoder.API.CoreSetup.Options;

/// <summary>
/// Root configuration options for CoreSetup. Bind to the "CoreSetup" section in appsettings.json.
/// </summary>
public sealed class CoreSetupOptions
{
    /// <summary>
    /// JWT Bearer authentication configuration.
    /// </summary>
    public JwtOptions Jwt { get; init; } = new();

    /// <summary>
    /// Swagger/OpenAPI documentation configuration.
    /// </summary>
    public SwaggerOptions Swagger { get; init; } = new();

    /// <summary>
    /// Optional Redis caching configuration. Set <see cref="RedisOptions.Enabled"/> to true to activate.
    /// </summary>
    public RedisOptions Redis { get; init; } = new();
}