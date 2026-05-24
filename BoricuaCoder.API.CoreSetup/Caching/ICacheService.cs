namespace BoricuaCoder.API.CoreSetup.Caching;

/// <summary>
/// Provides Redis cache operations scoped to the configured key prefix.
/// Inject this interface to manage cached data from application code.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or <c>default</c> if not found or Redis is unavailable.
    /// The key is resolved relative to the configured prefix.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> with the given <paramref name="ttl"/>.
    /// The key is resolved relative to the configured prefix. Failures are swallowed.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Returns all cache keys matching <paramref name="pattern"/> (e.g. "UserInfo:*").
    /// The pattern is automatically scoped to the configured prefix.
    /// </summary>
    Task<IEnumerable<string>> GetKeysAsync(string pattern, CancellationToken ct = default);

    /// <summary>
    /// Deletes the single cache entry at <paramref name="key"/> (relative to the configured prefix).
    /// </summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes every cache key that starts with the configured prefix.
    /// </summary>
    Task DeleteAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes the exact key and all descendant keys sharing its path segment.
    /// For example, <c>DeleteCascadeAsync("UserInfo:123")</c> removes
    /// <c>{prefix}UserInfo:123</c> and everything matching <c>{prefix}UserInfo:123:*</c>.
    /// </summary>
    Task DeleteCascadeAsync(string keySegment, CancellationToken ct = default);
}
