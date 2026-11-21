namespace BoricuaCoder.API.CoreSetup.Caching;

/// <summary>
/// Controls whether a cached response is shared by every caller or kept separate per user.
/// </summary>
public enum CacheUserScope
{
    /// <summary>
    /// Per-user when the request is authenticated or the endpoint requires authorization
    /// (its metadata carries any <c>IAuthorizeData</c>, such as <c>[Authorize]</c>); shared otherwise.
    /// This is the default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always per-user. Authenticated callers are keyed by a hash of their <c>sub</c> (or
    /// <c>NameIdentifier</c>) claim; anonymous callers share a single anonymous entry.
    /// </summary>
    PerUser = 1,

    /// <summary>
    /// One entry shared by every caller, regardless of authentication. Use only for responses
    /// that contain no user-specific data.
    /// </summary>
    Shared = 2
}
