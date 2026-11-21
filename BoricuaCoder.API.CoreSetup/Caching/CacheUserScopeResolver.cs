using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BoricuaCoder.API.CoreSetup.Caching;

/// <summary>Outcome of resolving the user part of a cache key.</summary>
/// <param name="CanCache">False when the request must not be cached (authenticated user without an id claim).</param>
/// <param name="UserSegment">
/// The scope marker that ends every key: <c>%u</c> + 32 hex for a user, <c>%a</c> for the anonymous
/// per-user bucket, <c>%s</c> for a shared entry. Null only when <paramref name="CanCache"/> is false.
/// </param>
internal readonly record struct CacheUserScopeResolution(bool CanCache, string? UserSegment);

internal static class CacheUserScopeResolver
{
    // Markers live in the reserved %-space: an argument is percent-escaped, so a literal '%' becomes
    // "%25" and no argument can ever produce one of these segments.
    internal const string SharedSegment = "%s";
    internal const string AnonymousSegment = "%a";
    internal const string UserMarker = "%u";
    private const string SubClaim = "sub";

    internal static CacheUserScopeResolution Resolve(HttpContext httpContext, CacheUserScope scope)
    {
        if (scope == CacheUserScope.Shared)
            return new CacheUserScopeResolution(true, SharedSegment);

        var user = httpContext.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated == true;

        if (scope == CacheUserScope.Auto && !isAuthenticated && !RequiresAuthorization(httpContext))
            return new CacheUserScopeResolution(true, SharedSegment);

        if (!isAuthenticated)
            return new CacheUserScopeResolution(true, AnonymousSegment);

        var id = user!.FindFirst(SubClaim)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(id))
            return new CacheUserScopeResolution(false, null);

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32];
        return new CacheUserScopeResolution(true, UserMarker + hash);
    }

    private static bool RequiresAuthorization(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null;
}
