using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoricuaCoder.API.CoreSetup.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BoricuaCoder.API.CoreSetup.Tests.Caching;

public class CacheKeyGeneratorTests
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Default;

    private sealed record Filter(string Q);

    public interface IFakeService;

    private sealed class FakeService : IFakeService;

    // ── EncodeArg ────────────────────────────────────────────────────────────

    [Fact]
    public void EncodeArg_Null_IsPercentToken() => Assert.Equal("%", CacheKeyGenerator.EncodeArg(null, Json));

    [Fact]
    public void EncodeArg_EscapesSeparatorAndGlob()
        => Assert.Equal("42%3Apermissions%2A", CacheKeyGenerator.EncodeArg("42:permissions*", Json));

    [Fact]
    public void EncodeArg_DistinctDtos_ProduceDistinctHashes()
        => Assert.NotEqual(CacheKeyGenerator.EncodeArg(new Filter("a"), Json), CacheKeyGenerator.EncodeArg(new Filter("b"), Json));

    [Fact]
    public void EncodeArg_Dto_StartsWithHashMarker()
        => Assert.StartsWith("%h", CacheKeyGenerator.EncodeArg(new Filter("a"), Json));

    [Fact]
    public void EncodeArg_LongString_IsHashed()
    {
        var encoded = CacheKeyGenerator.EncodeArg(new string('x', 65), Json);

        Assert.NotNull(encoded);
        Assert.Matches("^%h[0-9a-f]{32}$", encoded);
    }

    [Fact]
    public void EncodeArg_Primitives_UseInvariantCulture()
    {
        Assert.Equal("42", CacheKeyGenerator.EncodeArg(42, Json));
        Assert.Equal("1.5", CacheKeyGenerator.EncodeArg(1.5m, Json));
    }

    [Fact]
    public void EncodeArg_DateTime_UsesRoundTripFormat()
    {
        var dt = new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal(Uri.EscapeDataString(dt.ToString("O")), CacheKeyGenerator.EncodeArg(dt, Json));
    }

    [Fact]
    public void EncodeArg_UnserializableValue_ReturnsNull()
    {
        // A self-referencing graph throws a JsonException with default options.
        var node = new Node();
        node.Next = node;

        Assert.Null(CacheKeyGenerator.EncodeArg(node, Json));
    }

    private sealed class Node
    {
        public Node? Next { get; set; }
    }

    // ── Generate (MVC ActionExecutingContext) ─────────────────────────────────

    [Fact]
    public void Generate_Mvc_NoCustomKey_UsesControllerAndAction()
    {
        var context = BuildMvcContext("Products", "GetAll", []);

        Assert.Equal("Products::GetAll", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_NullPositionMatters()
    {
        var first = BuildMvcContext("Orders", "Search", [("a", null), ("b", 5)]);
        var second = BuildMvcContext("Orders", "Search", [("a", 5), ("b", null)]);

        var k1 = CacheKeyGenerator.Generate(null, first, null);
        var k2 = CacheKeyGenerator.Generate(null, second, null);

        Assert.NotEqual(k1, k2);
        Assert.Equal("Orders::Search:%:5", k1);
        Assert.Equal("Orders::Search:5:%", k2);
    }

    [Fact]
    public void Generate_Mvc_MissingArgument_CountsAsNull()
    {
        var context = BuildMvcContext("Orders", "Search", [("a", 1), ("b", 2)]);
        context.ActionArguments.Remove("a");

        Assert.Equal("Orders::Search:%:2", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_CustomKey_AppendsEndpointIdAfterArgs()
    {
        var context = BuildMvcContext("Users", "GetPermissions", [("id", 42)]);

        Assert.Equal("UserInfo:42:Users::GetPermissions", CacheKeyGenerator.Generate("UserInfo", context, null));
    }

    [Fact]
    public void Generate_Mvc_SameCustomKeyDifferentActions_DoNotCollide()
    {
        var get = BuildMvcContext("Users", "Get", [("id", 42)]);
        var perms = BuildMvcContext("Users", "GetPermissions", [("id", 42)]);

        var k1 = CacheKeyGenerator.Generate("UserInfo", get, null);
        var k2 = CacheKeyGenerator.Generate("UserInfo", perms, null);

        Assert.NotEqual(k1, k2);
        Assert.StartsWith("UserInfo:42:", k1);
        Assert.StartsWith("UserInfo:42:", k2);
    }

    [Fact]
    public void Generate_Mvc_UserSegment_IsLast()
    {
        var context = BuildMvcContext("Users", "Me", [("id", 1)]);

        var key = CacheKeyGenerator.Generate("UserInfo", context, "u-abc");

        Assert.NotNull(key);
        Assert.EndsWith(":u-abc", key);
        Assert.Equal("UserInfo:1:Users::Me:u-abc", key);
    }

    [Fact]
    public void Generate_Mvc_SkipsCancellationToken()
    {
        var context = BuildMvcContext("Orders", "List", [("ct", CancellationToken.None)]);

        Assert.Equal("Orders::List", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_NullRouteController_FallsBackToUnknown()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var execContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

        Assert.Equal("Unknown::Unknown", CacheKeyGenerator.Generate(null, execContext, null));
    }

    [Fact]
    public void Generate_Mvc_UnencodableArgument_ReturnsNull()
    {
        var node = new Node();
        node.Next = node;
        var context = BuildMvcContext("Orders", "Search", [("filter", node)]);

        Assert.Null(CacheKeyGenerator.Generate(null, context, null));
    }

    // ── Generate (EndpointFilterInvocationContext) ────────────────────────────

    [Fact]
    public void Generate_Endpoint_NoCustomKey_UsesEndpointName()
    {
        var context = BuildEndpointContext(endpointName: "GetProducts", args: [7]);

        Assert.Equal("GetProducts:7", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_CustomKey_AppendsEndpointIdAfterArgs()
    {
        var context = BuildEndpointContext(endpointName: "GetUser", args: [42]);

        Assert.Equal("UserInfo:42:GetUser:u-abc", CacheKeyGenerator.Generate("UserInfo", context, "u-abc"));
    }

    [Fact]
    public void Generate_Endpoint_SkipsDiServicesAndClaimsPrincipal()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeService, FakeService>();
        services.AddSingleton<FakeService>();
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var context = BuildEndpointContext(
            endpointName: "GetUser",
            args: [provider.GetRequiredService<FakeService>(), new ClaimsPrincipal(), httpContext, 99, CancellationToken.None],
            httpContext: httpContext);

        Assert.Equal("GetUser:99", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_UsesRoutePatternWhenNoName()
    {
        var context = BuildEndpointContext(routePattern: "/users/{id}", args: [42]);

        Assert.Equal("users_%7Bid%7D:42", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_NoEndpoint_FallsBackToRequestPath()
    {
        var context = BuildEndpointContext(path: "/products/list", args: []);

        Assert.Equal("products_list", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_NothingAvailable_FallsBackToUnknown()
    {
        var context = BuildEndpointContext(args: []);

        Assert.Equal("unknown", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_LongArgs_HashedWithFullSha256()
    {
        var args = Enumerable.Range(0, 20).Select(i => (object?)$"{i:D2}{new string('v', 58)}").ToArray();
        var context = BuildEndpointContext(endpointName: "Search", args: args);

        var key = CacheKeyGenerator.Generate(null, context, "u-abc");

        Assert.NotNull(key);
        Assert.True(key.Length <= 512);
        Assert.Matches(new Regex("^Search:%h[0-9a-f]{64}:u-abc$"), key);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EndpointFilterInvocationContext BuildEndpointContext(
        IEnumerable<object?>? args = null,
        string? endpointName = null,
        string? routePattern = null,
        string? path = null,
        HttpContext? httpContext = null)
    {
        httpContext ??= new DefaultHttpContext();

        if (endpointName is not null)
        {
            httpContext.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new EndpointNameMetadata(endpointName)),
                endpointName));
        }
        else if (routePattern is not null)
        {
            httpContext.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(routePattern),
                0,
                EndpointMetadataCollection.Empty,
                routePattern));
        }

        if (path is not null)
            httpContext.Request.Path = path;

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        context.Arguments.Returns(new List<object?>(args ?? []));
        return context;
    }

    private static ActionExecutingContext BuildMvcContext(
        string controller,
        string action,
        (string Name, object? Value)[] args)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["controller"] = controller;
        httpContext.Request.RouteValues["action"] = action;
        var routeData = new RouteData(httpContext.Request.RouteValues);

        var descriptor = new ActionDescriptor
        {
            Parameters = args
                .Select(a => new ParameterDescriptor
                {
                    Name = a.Name,
                    ParameterType = a.Value?.GetType() ?? typeof(object)
                })
                .ToList()
        };

        var actionArgs = args.ToDictionary(a => a.Name, a => a.Value);
        var actionContext = new ActionContext(httpContext, routeData, descriptor);
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), actionArgs, new object());
    }
}
