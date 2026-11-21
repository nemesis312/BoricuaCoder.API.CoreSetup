using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoricuaCoder.API.CoreSetup.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using NSubstitute;
using AdminUsers = BoricuaCoder.API.CoreSetup.Tests.Caching.Poc.Admin.UsersController;
using PublicUsers = BoricuaCoder.API.CoreSetup.Tests.Caching.Poc.Public.UsersController;

namespace BoricuaCoder.API.CoreSetup.Tests.Caching;

/// <summary>
/// F1 regression tests: a literal argument must never reproduce another endpoint's or another
/// user's key (scope marker always last and taken from the reserved %-space, unique endpoint id).
/// </summary>
public class CacheKeyScopeRegressionTests
{
    private static string SubHash(string sub)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sub)))[..32];

    // ── The exact PoC ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("u-")]
    [InlineData("%u")]
    public void Poc_AnonymousPublicStringArg_CannotForgeVictimAdminKey(string forgedPrefix)
    {
        var victimHttp = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "42")], authenticationType: "Test"))
        };
        SetEndpoint(victimHttp, new AuthorizeAttribute());
        var victimScope = CacheUserScopeResolver.Resolve(victimHttp, CacheUserScope.Auto);
        var victim = CacheKeyGenerator.Generate(null,
            BuildControllerContext(typeof(AdminUsers), "Admin", new() { ["id"] = 42 }, victimHttp),
            victimScope.UserSegment);

        var attackerHttp = new DefaultHttpContext();
        SetEndpoint(attackerHttp);
        var attackerScope = CacheUserScopeResolver.Resolve(attackerHttp, CacheUserScope.Auto);
        var attacker = CacheKeyGenerator.Generate(null,
            BuildControllerContext(typeof(PublicUsers), null,
                new() { ["id"] = 42, ["lang"] = forgedPrefix + SubHash("42") }, attackerHttp),
            attackerScope.UserSegment);

        Assert.NotNull(victim);
        Assert.NotNull(attacker);
        Assert.NotEqual(victim, attacker);
        Assert.Matches("^Users::Get%i[0-9a-f]{8}:42:%u[0-9a-f]{32}$", victim);
        Assert.EndsWith(":%s", attacker);
    }

    [Fact]
    public void SameControllerAndActionName_DifferentNamespacesAndAreas_DifferentEndpointIds()
    {
        var admin = CacheKeyGenerator.Generate(null,
            BuildControllerContext(typeof(AdminUsers), "Admin", new() { ["id"] = 42 }), "%s");
        var pub = CacheKeyGenerator.Generate(null,
            BuildControllerContext(typeof(PublicUsers), null, new() { ["id"] = 42, ["lang"] = null }), "%s");

        Assert.NotNull(admin);
        Assert.NotNull(pub);
        Assert.StartsWith("Users::Get%i", admin);
        Assert.StartsWith("Users::Get%i", pub);
        Assert.NotEqual(admin.Split(':')[..3], pub.Split(':')[..3]);
    }

    [Fact]
    public void SameController_DifferentAreaRouteValue_DifferentKeys()
    {
        var a = CacheKeyGenerator.Generate(null, BuildControllerContext(typeof(AdminUsers), "Admin", new() { ["id"] = 1 }), "%s");
        var b = CacheKeyGenerator.Generate(null, BuildControllerContext(typeof(AdminUsers), "Other", new() { ["id"] = 1 }), "%s");

        Assert.NotEqual(a, b);
    }

    // ── Minimal API: same route pattern, different host ──────────────────────

    [Fact]
    public void Endpoint_SameRawTextDifferentRequireHost_DifferentKeys()
    {
        var a = CacheKeyGenerator.Generate(null, BuildRouteEndpointContext("/users/{id}", "a.example.com"), "%s");
        var b = CacheKeyGenerator.Generate(null, BuildRouteEndpointContext("/users/{id}", "b.example.com"), "%s");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
        Assert.Matches("^users_%7Bid%7D%i[0-9a-f]{8}:42:%s$", a);
    }

    [Fact]
    public void Endpoint_HostOrder_DoesNotChangeKey()
    {
        var a = CacheKeyGenerator.Generate(null, BuildRouteEndpointContext("/users/{id}", "a.example.com", "b.example.com"), "%s");
        var b = CacheKeyGenerator.Generate(null, BuildRouteEndpointContext("/users/{id}", "b.example.com", "a.example.com"), "%s");

        Assert.Equal(a, b);
    }

    // ── Scope marker always present ──────────────────────────────────────────

    [Fact]
    public void SharedAndAutoAnonymousPublic_BothEndWithSharedMarker()
    {
        var authenticated = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], authenticationType: "Test"))
        };
        SetEndpoint(authenticated);
        var anonymous = new DefaultHttpContext();
        SetEndpoint(anonymous);

        var shared = CacheUserScopeResolver.Resolve(authenticated, CacheUserScope.Shared);
        var autoAnon = CacheUserScopeResolver.Resolve(anonymous, CacheUserScope.Auto);

        Assert.Equal("%s", shared.UserSegment);
        Assert.Equal("%s", autoAnon.UserSegment);

        var context = BuildControllerContext(typeof(PublicUsers), null, new() { ["id"] = 1, ["lang"] = "en" });
        Assert.EndsWith(":%s", CacheKeyGenerator.Generate(null, context, shared.UserSegment));
        Assert.EndsWith(":%s", CacheKeyGenerator.Generate(null, context, autoAnon.UserSegment));
    }

    [Fact]
    public void PerUserAnonymous_EndsWithAnonymousMarker()
    {
        var anonymous = new DefaultHttpContext();
        SetEndpoint(anonymous);

        var scope = CacheUserScopeResolver.Resolve(anonymous, CacheUserScope.PerUser);
        var key = CacheKeyGenerator.Generate(null,
            BuildControllerContext(typeof(PublicUsers), null, new() { ["id"] = 1, ["lang"] = null }), scope.UserSegment);

        Assert.Equal("%a", scope.UserSegment);
        Assert.EndsWith(":%a", key);
    }

    [Fact]
    public void PerUser_Authenticated_UsesUserMarker()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], authenticationType: "Test"))
        };
        SetEndpoint(http);

        var scope = CacheUserScopeResolver.Resolve(http, CacheUserScope.PerUser);

        Assert.Equal("%u" + SubHash("alice"), scope.UserSegment);
    }

    [Theory]
    [InlineData("%s", "%25s")]
    [InlineData("%a", "%25a")]
    [InlineData("%u0123", "%25u0123")]
    [InlineData("%i0123", "%25i0123")]
    public void LiteralMarkerArg_IsEscaped(string literal, string expected)
    {
        Assert.Equal(expected, CacheKeyGenerator.EncodeArg(literal, JsonSerializerOptions.Default));

        var key = CacheKeyGenerator.Generate(null,
            BuildControllerContext(typeof(PublicUsers), null, new() { ["id"] = 1, ["lang"] = literal }), "%s");

        Assert.NotNull(key);
        Assert.EndsWith($":1:{expected}:%s", key);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SetEndpoint(HttpContext http, params object[] metadata)
        => http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));

    internal static ActionExecutingContext BuildControllerContext(
        Type controllerType,
        string? area,
        Dictionary<string, object?> args,
        HttpContext? httpContext = null)
    {
        httpContext ??= new DefaultHttpContext();
        httpContext.Request.RouteValues["controller"] = "Users";
        httpContext.Request.RouteValues["action"] = "Get";
        if (area is not null)
            httpContext.Request.RouteValues["area"] = area;
        var routeData = new RouteData(httpContext.Request.RouteValues);

        var method = controllerType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance)!;
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "Users",
            ActionName = "Get",
            ControllerTypeInfo = controllerType.GetTypeInfo(),
            MethodInfo = method,
            Parameters = method.GetParameters()
                .Select(p => (ParameterDescriptor)new ControllerParameterDescriptor
                {
                    Name = p.Name!,
                    ParameterType = p.ParameterType,
                    ParameterInfo = p
                })
                .ToList()
        };

        var actionContext = new ActionContext(httpContext, routeData, descriptor);
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, new object());
    }

    private static EndpointFilterInvocationContext BuildRouteEndpointContext(string pattern, params string[] hosts)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            0,
            new EndpointMetadataCollection(new HostAttribute(hosts)),
            pattern));

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        context.Arguments.Returns(new List<object?> { 42 });
        return context;
    }
}
