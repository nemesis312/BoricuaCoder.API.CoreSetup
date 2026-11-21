using System.Security.Claims;
using BoricuaCoder.API.CoreSetup.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BoricuaCoder.API.CoreSetup.Tests.Caching;

public class CacheUserScopeResolverTests
{
    [Fact]
    public void Resolve_Shared_ReturnsSharedMarker()
    {
        var ctx = BuildContext(authenticated: true, claims: [new Claim("sub", "alice")]);

        var result = CacheUserScopeResolver.Resolve(ctx, CacheUserScope.Shared);

        Assert.True(result.CanCache);
        Assert.Equal("%s", result.UserSegment);
    }

    [Fact]
    public void Resolve_Auto_Anonymous_NoAuthorizeMetadata_ReturnsSharedMarker()
    {
        var ctx = BuildContext(authenticated: false);

        var result = CacheUserScopeResolver.Resolve(ctx, CacheUserScope.Auto);

        Assert.True(result.CanCache);
        Assert.Equal("%s", result.UserSegment);
    }

    [Fact]
    public void Resolve_Auto_Authenticated_UsesSubHash()
    {
        var alice1 = CacheUserScopeResolver.Resolve(
            BuildContext(authenticated: true, claims: [new Claim("sub", "alice")]), CacheUserScope.Auto);
        var alice2 = CacheUserScopeResolver.Resolve(
            BuildContext(authenticated: true, claims: [new Claim("sub", "alice")]), CacheUserScope.Auto);
        var bob = CacheUserScopeResolver.Resolve(
            BuildContext(authenticated: true, claims: [new Claim("sub", "bob")]), CacheUserScope.Auto);

        Assert.True(alice1.CanCache);
        Assert.NotNull(alice1.UserSegment);
        Assert.Matches("^%u[0-9a-f]{32}$", alice1.UserSegment);
        Assert.Equal(alice1.UserSegment, alice2.UserSegment);
        Assert.NotEqual(alice1.UserSegment, bob.UserSegment);
    }

    [Fact]
    public void Resolve_Auto_AnonymousOnAuthorizeEndpoint_ReturnsAnon()
    {
        var ctx = BuildContext(authenticated: false, requireAuthorization: true);

        var result = CacheUserScopeResolver.Resolve(ctx, CacheUserScope.Auto);

        Assert.True(result.CanCache);
        Assert.Equal("%a", result.UserSegment);
    }

    [Fact]
    public void Resolve_PerUser_Anonymous_ReturnsAnon()
    {
        var ctx = BuildContext(authenticated: false);

        var result = CacheUserScopeResolver.Resolve(ctx, CacheUserScope.PerUser);

        Assert.True(result.CanCache);
        Assert.Equal("%a", result.UserSegment);
    }

    [Fact]
    public void Resolve_AuthenticatedWithoutIdClaim_CannotCache()
    {
        var ctx = BuildContext(authenticated: true, claims: [new Claim("email", "a@b.c")]);

        var result = CacheUserScopeResolver.Resolve(ctx, CacheUserScope.Auto);

        Assert.False(result.CanCache);
    }

    [Fact]
    public void Resolve_PrefersSubOverNameIdentifier()
    {
        var both = CacheUserScopeResolver.Resolve(
            BuildContext(authenticated: true, claims:
            [
                new Claim(ClaimTypes.NameIdentifier, "nameid-value"),
                new Claim("sub", "sub-value")
            ]),
            CacheUserScope.PerUser);
        var subOnly = CacheUserScopeResolver.Resolve(
            BuildContext(authenticated: true, claims: [new Claim("sub", "sub-value")]), CacheUserScope.PerUser);
        var nameIdOnly = CacheUserScopeResolver.Resolve(
            BuildContext(authenticated: true, claims: [new Claim(ClaimTypes.NameIdentifier, "nameid-value")]),
            CacheUserScope.PerUser);

        Assert.Equal(subOnly.UserSegment, both.UserSegment);
        Assert.NotEqual(nameIdOnly.UserSegment, both.UserSegment);
        Assert.Matches("^%u[0-9a-f]{32}$", nameIdOnly.UserSegment!);
    }

    private static HttpContext BuildContext(
        bool authenticated,
        Claim[]? claims = null,
        bool requireAuthorization = false)
    {
        var identity = authenticated
            ? new ClaimsIdentity(claims ?? [], authenticationType: "Test")
            : new ClaimsIdentity(claims ?? []);

        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var metadata = requireAuthorization
            ? new EndpointMetadataCollection(new AuthorizeAttribute())
            : EndpointMetadataCollection.Empty;
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        return ctx;
    }
}
