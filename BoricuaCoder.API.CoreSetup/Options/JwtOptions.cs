namespace BoricuaCoder.API.CoreSetup.Options;

/// <summary>
/// Configuration options for JWT Bearer authentication.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// The URL of the identity provider (e.g., Auth0, IdentityServer, Keycloak).
    /// </summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// The expected audience claim in the JWT token.
    /// </summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Whether to require HTTPS for metadata retrieval. Set to false for local development with HTTP identity providers.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;
}