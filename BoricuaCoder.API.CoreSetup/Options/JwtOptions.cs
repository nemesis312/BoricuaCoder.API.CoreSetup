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

    /// <summary>
    /// Fine-grained token validation overrides. Only properties that are explicitly set will override the JWT Bearer defaults.
    /// </summary>
    public TokenValidationOptions TokenValidation { get; init; } = new();
}

/// <summary>
/// Fine-grained overrides for JWT token validation. Leave a property <c>null</c> to keep the JWT Bearer default behavior.
/// </summary>
public sealed class TokenValidationOptions
{
    /// <summary>
    /// Override whether the token issuer is validated.
    /// When <c>null</c>, the JWT Bearer handler decides (typically <c>true</c> when <c>Authority</c> is set).
    /// </summary>
    public bool? ValidateIssuer { get; init; }

    /// <summary>
    /// Override whether the token audience is validated.
    /// When <c>null</c>, the JWT Bearer handler decides (typically <c>true</c>).
    /// </summary>
    public bool? ValidateAudience { get; init; }

    /// <summary>
    /// Override whether the token lifetime (expiration) is validated.
    /// When <c>null</c>, the JWT Bearer handler decides (typically <c>true</c>).
    /// </summary>
    public bool? ValidateLifetime { get; init; }

    /// <summary>
    /// Clock skew tolerance in seconds applied when validating token expiration.
    /// When <c>null</c>, the JWT Bearer default of 300 seconds (5 minutes) is used.
    /// Set to <c>0</c> to enforce exact expiration times.
    /// </summary>
    public int? ClockSkewSeconds { get; init; }
}