namespace BoricuaCoder.API.CoreSetup.Options;

/// <summary>
/// Redis caching configuration for CoreSetup. Bind to the "CoreSetup:Redis" section in appsettings.json.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// Enables or disables Redis caching. Default: false.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Prefix applied to all cache keys managed by this application.
    /// Example: "myapp:api::". If empty, keys are stored without a prefix.
    /// </summary>
    public string PrefixKey { get; init; } = string.Empty;

    /// <summary>
    /// Redis connection string (e.g. "localhost:6379" or "redis://user:pass@host:6379").
    /// Required when <see cref="Enabled"/> is true.
    /// </summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Default time-to-live in seconds for cached entries when not specified in [Cache].
    /// Default: 300 (5 minutes).
    /// </summary>
    public int DefaultTTL { get; init; } = 300;

    /// <summary>
    /// Maximum seconds to wait for a Redis response before bypassing the cache and calling through to the handler.
    /// 0 means no short-circuit timeout. Default: 0.
    /// </summary>
    public int ShortCircuit { get; init; } = 0;
}
