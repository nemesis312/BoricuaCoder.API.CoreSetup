namespace BoricuaCoder.API.CoreSetup.Options;

/// <summary>
/// Configuration options for Swagger/OpenAPI documentation.
/// </summary>
public sealed class SwaggerOptions
{
    /// <summary>
    /// Whether Swagger UI is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The title displayed in Swagger UI. Default is "API".
    /// </summary>
    public string Title { get; init; } = "API";

    /// <summary>
    /// The API version for the Swagger document. Default is "v1".
    /// </summary>
    public string Version { get; init; } = "v1";

    /// <summary>
    /// The URL prefix for Swagger UI (e.g., "swagger" for /swagger). Default is "swagger".
    /// </summary>
    public string RoutePrefix { get; init; } = "swagger";

    /// <summary>
    /// OAuth2 configuration for Swagger authentication.
    /// </summary>
    public SwaggerOAuthOptions OAuth { get; init; } = new();
}

/// <summary>
/// OAuth2 configuration for Swagger UI with Keycloak.
/// </summary>
public sealed class SwaggerOAuthOptions
{
    /// <summary>
    /// The OAuth2 authorization endpoint URL (Keycloak auth endpoint).
    /// Example: https://keycloak.example.com/realms/{realm}/protocol/openid-connect/auth
    /// </summary>
    public string AuthorizationUrl { get; init; } = string.Empty;

    /// <summary>
    /// The OAuth2 token endpoint URL (Keycloak token endpoint).
    /// Example: https://keycloak.example.com/realms/{realm}/protocol/openid-connect/token
    /// </summary>
    public string TokenUrl { get; init; } = string.Empty;

    /// <summary>
    /// The OAuth2 client ID registered in Keycloak.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Available scopes that users can select. Key is scope name, value is description.
    /// Example: { "openid": "OpenID Connect", "profile": "User profile", "email": "Email address" }
    /// </summary>
    public Dictionary<string, string> Scopes { get; init; } = new()
    {
        { "openid", "OpenID Connect" }
    };
}