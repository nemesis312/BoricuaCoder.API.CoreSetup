using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoricuaCoder.API.CoreSetup.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

        Assert.Matches("^Products::GetAll%i[0-9a-f]{8}:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_NullPositionMatters()
    {
        var first = BuildMvcContext("Orders", "Search", [("a", null), ("b", 5)]);
        var second = BuildMvcContext("Orders", "Search", [("a", 5), ("b", null)]);

        var k1 = CacheKeyGenerator.Generate(null, first, null);
        var k2 = CacheKeyGenerator.Generate(null, second, null);

        Assert.NotEqual(k1, k2);
        Assert.Matches("^Orders::Search%i[0-9a-f]{8}:%:5:%s$", k1);
        Assert.Matches("^Orders::Search%i[0-9a-f]{8}:5:%:%s$", k2);
    }

    [Fact]
    public void Generate_Mvc_MissingArgument_CountsAsNull()
    {
        var context = BuildMvcContext("Orders", "Search", [("a", 1), ("b", 2)]);
        context.ActionArguments.Remove("a");

        Assert.Matches("^Orders::Search%i[0-9a-f]{8}:%:2:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_CustomKey_AppendsEndpointIdAfterArgs()
    {
        var context = BuildMvcContext("Users", "GetPermissions", [("id", 42)]);

        Assert.Matches("^UserInfo:42:Users::GetPermissions%i[0-9a-f]{8}:%s$", CacheKeyGenerator.Generate("UserInfo", context, null));
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

        var key = CacheKeyGenerator.Generate("UserInfo", context, "%uabc");

        Assert.NotNull(key);
        Assert.EndsWith(":%uabc", key);
        Assert.Matches("^UserInfo:1:Users::Me%i[0-9a-f]{8}:%uabc$", key);
    }

    [Fact]
    public void Generate_Mvc_SkipsCancellationToken()
    {
        var context = BuildMvcContext("Orders", "List", [("ct", CancellationToken.None)]);

        Assert.Matches("^Orders::List%i[0-9a-f]{8}:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_NullRouteController_FallsBackToUnknown()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var execContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

        Assert.Matches("^Unknown::Unknown%i[0-9a-f]{8}:%s$", CacheKeyGenerator.Generate(null, execContext, null));
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

        Assert.Matches("^GetProducts%i[0-9a-f]{8}:7:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_CustomKey_AppendsEndpointIdAfterArgs()
    {
        var context = BuildEndpointContext(endpointName: "GetUser", args: [42]);

        Assert.Matches("^UserInfo:42:GetUser%i[0-9a-f]{8}:%uabc$", CacheKeyGenerator.Generate("UserInfo", context, "%uabc"));
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

        Assert.Matches("^GetUser%i[0-9a-f]{8}:99:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_UsesRoutePatternWhenNoName()
    {
        var context = BuildEndpointContext(routePattern: "/users/{id}", args: [42]);

        Assert.Matches("^users_%7Bid%7D%i[0-9a-f]{8}:42:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_NoEndpoint_FallsBackToRequestPath()
    {
        var context = BuildEndpointContext(path: "/products/list", args: []);

        Assert.Matches("^products_list%i[0-9a-f]{8}:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_NothingAvailable_FallsBackToUnknown()
    {
        var context = BuildEndpointContext(args: []);

        Assert.Matches("^unknown%i[0-9a-f]{8}:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_LongArgs_HashedWithFullSha256()
    {
        var args = Enumerable.Range(0, 20).Select(i => (object?)$"{i:D2}{new string('v', 58)}").ToArray();
        var context = BuildEndpointContext(endpointName: "Search", args: args);

        var key = CacheKeyGenerator.Generate(null, context, "%uabc");

        Assert.NotNull(key);
        Assert.True(key.Length <= 512);
        Assert.Matches(new Regex("^Search%i[0-9a-f]{8}:%h[0-9a-f]{64}:%uabc$"), key);
    }

    // ── Fix round 1: service detection, empty strings, length ────────────────

    [Fact]
    public void EncodeArg_EmptyString_IsReservedToken() => Assert.Equal("%e", CacheKeyGenerator.EncodeArg("", Json));

    [Fact]
    public void Generate_EmptyArgWithUserSegment_DiffersFromForgedUserArg()
    {
        var victim = CacheKeyGenerator.Generate(null, BuildEndpointContext(endpointName: "search", args: [""]), "%uX");
        var forged = CacheKeyGenerator.Generate(null, BuildEndpointContext(endpointName: "search", args: ["%uX"]), null);

        Assert.Matches("^search%i[0-9a-f]{8}:%e:%uX$", victim);
        Assert.Matches("^search%i[0-9a-f]{8}:%25uX:%s$", forged);
        Assert.NotEqual(victim, forged);
    }

    [Fact]
    public void Generate_Mvc_EnumerableParam_IsPartOfKey()
    {
        var provider = BuildProviderWithFakeService();
        var one = BuildMvcContext("Orders", "ByIds",
            [Param("ids", typeof(IEnumerable<int>))], new() { ["ids"] = new[] { 1 } }, provider);
        var two = BuildMvcContext("Orders", "ByIds",
            [Param("ids", typeof(IEnumerable<int>))], new() { ["ids"] = new[] { 2 } }, provider);

        var k1 = CacheKeyGenerator.Generate(null, one, null);
        var k2 = CacheKeyGenerator.Generate(null, two, null);

        Assert.NotEqual(k1, k2);
        Assert.Matches("^Orders::ByIds%i[0-9a-f]{8}:%h[0-9a-f]{32}:%s$", k1!);
    }

    [Fact]
    public void Generate_Mvc_FromServicesParam_IsExcluded()
    {
        // Node is not registered in DI: only the [FromServices] binding source marks it as a service.
        var context = BuildMvcContext("Users", "Get",
            [
                Param("svc", typeof(Node), new BindingInfo { BindingSource = BindingSource.Services }),
                Param("id", typeof(int))
            ],
            new() { ["svc"] = new Node(), ["id"] = 5 },
            BuildProviderWithFakeService());

        Assert.Matches("^Users::Get%i[0-9a-f]{8}:5:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Mvc_RegisteredInterfaceParam_IsExcluded()
    {
        var context = BuildMvcContext("Users", "Get",
            [Param("svc", typeof(IFakeService)), Param("id", typeof(int))],
            new() { ["svc"] = new FakeService(), ["id"] = 5 },
            BuildProviderWithFakeService());

        Assert.Matches("^Users::Get%i[0-9a-f]{8}:5:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_InjectedInterface_ExcludedUsingHandlerSignature()
    {
        // Only IFakeService is registered; the runtime type FakeService is not, so the declared
        // handler parameter type is what identifies the argument as a service.
        var httpContext = new DefaultHttpContext { RequestServices = BuildProviderWithFakeService(isConcreteRegistered: false) };
        var context = BuildEndpointContext(
            endpointName: "GetUser",
            args: [new FakeService(), 5],
            httpContext: httpContext,
            metadata: [HandlerMethod(nameof(HandlerWithService))]);

        Assert.Matches("^GetUser%i[0-9a-f]{8}:5:%s$", CacheKeyGenerator.Generate(null, context, null));
    }

    [Fact]
    public void Generate_Endpoint_CollectionArgs_AreIncluded()
    {
        var provider = BuildProviderWithFakeService();
        EndpointFilterInvocationContext Build(int a, int b) => BuildEndpointContext(
            endpointName: "ByIds",
            args: [new[] { a }, new List<int> { b }],
            httpContext: new DefaultHttpContext { RequestServices = provider },
            metadata: [HandlerMethod(nameof(HandlerWithCollections))]);

        var k1 = CacheKeyGenerator.Generate(null, Build(1, 2), null);
        var k2 = CacheKeyGenerator.Generate(null, Build(1, 3), null);

        Assert.Matches("^ByIds%i[0-9a-f]{8}:%h[0-9a-f]{32}:%h[0-9a-f]{32}:%s$", k1!);
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void Generate_Endpoint_CollectionArgs_IncludedWithoutHandlerSignature()
    {
        var httpContext = new DefaultHttpContext { RequestServices = BuildProviderWithFakeService() };
        var context = BuildEndpointContext(endpointName: "ByIds", args: [new List<int> { 1 }], httpContext: httpContext);

        Assert.Matches("^ByIds%i[0-9a-f]{8}:%h[0-9a-f]{32}:%s$", CacheKeyGenerator.Generate(null, context, null)!);
    }

    [Fact]
    public void Generate_LongEndpointId_IsHashedAfterArgs()
    {
        var context = BuildEndpointContext(endpointName: new string('e', 600), args: [42]);

        var key = CacheKeyGenerator.Generate("UserInfo", context, "%uabc");

        Assert.NotNull(key);
        Assert.True(key.Length <= 512);
        Assert.Matches("^UserInfo:%h[0-9a-f]{64}:%h[0-9a-f]{64}:%uabc$", key);
    }

    [Fact]
    public void Generate_LongCustomKey_IsHashedWhenStillTooLong()
    {
        var context = BuildEndpointContext(endpointName: "GetUser", args: [42]);

        var key = CacheKeyGenerator.Generate(new string('c', 600), context, "%uabc");

        Assert.NotNull(key);
        Assert.True(key.Length <= 512);
        Assert.Matches("^%h[0-9a-f]{64}:%h[0-9a-f]{64}:GetUser%i[0-9a-f]{8}:%uabc$", key);
    }

    private static void HandlerWithService(IFakeService svc, int id) { }

    private static void HandlerWithCollections(int[] a, IEnumerable<int> b) { }

    private static MethodInfo HandlerMethod(string name)
        => typeof(CacheKeyGeneratorTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static ServiceProvider BuildProviderWithFakeService(bool isConcreteRegistered = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFakeService, FakeService>();
        if (isConcreteRegistered)
            services.AddSingleton<FakeService>();
        return services.BuildServiceProvider();
    }

    private static ParameterDescriptor Param(string name, Type type, BindingInfo? bindingInfo = null)
        => new() { Name = name, ParameterType = type, BindingInfo = bindingInfo };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EndpointFilterInvocationContext BuildEndpointContext(
        IEnumerable<object?>? args = null,
        string? endpointName = null,
        string? routePattern = null,
        string? path = null,
        HttpContext? httpContext = null,
        object[]? metadata = null)
    {
        httpContext ??= new DefaultHttpContext();

        if (endpointName is not null)
        {
            httpContext.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection([new EndpointNameMetadata(endpointName), .. metadata ?? []]),
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

    private static ActionExecutingContext BuildMvcContext(
        string controller,
        string action,
        ParameterDescriptor[] parameters,
        Dictionary<string, object?> args,
        IServiceProvider services)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.RouteValues["controller"] = controller;
        httpContext.Request.RouteValues["action"] = action;
        var routeData = new RouteData(httpContext.Request.RouteValues);
        var descriptor = new ActionDescriptor { Parameters = parameters.ToList() };
        var actionContext = new ActionContext(httpContext, routeData, descriptor);
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, new object());
    }
}
