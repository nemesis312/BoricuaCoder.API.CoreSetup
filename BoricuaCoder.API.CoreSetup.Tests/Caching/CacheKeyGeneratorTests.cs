using BoricuaCoder.API.CoreSetup.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace BoricuaCoder.API.CoreSetup.Tests.Caching;

public class CacheKeyGeneratorTests
{
    // ── BuildKey ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildKey_NoArgs_ReturnsPrefix_Segment()
    {
        var key = CacheKeyGenerator.BuildKey("myapp::", "Products::GetAll", "");

        Assert.Equal("myapp::Products::GetAll", key);
    }

    [Fact]
    public void BuildKey_WithArgs_AppendsSeparatorAndArgs()
    {
        var key = CacheKeyGenerator.BuildKey("myapp::", "Products::GetById", "42");

        Assert.Equal("myapp::Products::GetById:42", key);
    }

    [Fact]
    public void BuildKey_MultipleArgs_JoinsWithColon()
    {
        var key = CacheKeyGenerator.BuildKey("myapp::", "Orders::Get", "1:desc");

        Assert.Equal("myapp::Orders::Get:1:desc", key);
    }

    [Fact]
    public void BuildKey_EmptyPrefix_WorksCorrectly()
    {
        var key = CacheKeyGenerator.BuildKey("", "Products::GetAll", "");

        Assert.Equal("Products::GetAll", key);
    }

    [Fact]
    public void BuildKey_KeyExceedsMaxLength_ReturnsHashedKey()
    {
        var longSegment = new string('a', 400);
        var longArgs = new string('b', 200);

        var key = CacheKeyGenerator.BuildKey("myapp::", longSegment, longArgs);

        Assert.True(key.Length <= 512);
        Assert.StartsWith("myapp::", key);
    }

    [Fact]
    public void BuildKey_KeyAtExactMaxLength_IsNotHashed()
    {
        // Build a key that is exactly 512 chars
        var segmentLength = 512 - "myapp::".Length;
        var segment = new string('x', segmentLength);

        var key = CacheKeyGenerator.BuildKey("myapp::", segment, "");

        Assert.Equal(512, key.Length);
        Assert.Equal($"myapp::{segment}", key);
    }

    // ── BuildArgsSegment ─────────────────────────────────────────────────────

    [Fact]
    public void BuildArgsSegment_EmptyCollection_ReturnsEmptyString()
    {
        var result = CacheKeyGenerator.BuildArgsSegment([]);

        Assert.Equal("", result);
    }

    [Fact]
    public void BuildArgsSegment_SinglePart_ReturnsItDirectly()
    {
        var result = CacheKeyGenerator.BuildArgsSegment(["42"]);

        Assert.Equal("42", result);
    }

    [Fact]
    public void BuildArgsSegment_MultipleParts_JoinsWithColon()
    {
        var result = CacheKeyGenerator.BuildArgsSegment(["1", "active", "en"]);

        Assert.Equal("1:active:en", result);
    }

    [Fact]
    public void BuildArgsSegment_SkipsEmptyParts()
    {
        var result = CacheKeyGenerator.BuildArgsSegment(["1", "", "en"]);

        Assert.Equal("1:en", result);
    }

    // ── Generate (MVC ActionExecutingContext) ─────────────────────────────────

    [Fact]
    public void Generate_Mvc_NoCustomKey_UsesControllerAndAction()
    {
        var context = BuildMvcContext("Products", "GetAll", new Dictionary<string, object?>());

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::Products::GetAll", key);
    }

    [Fact]
    public void Generate_Mvc_WithIntArg_AppendsValue()
    {
        var context = BuildMvcContext("Products", "GetById",
            new Dictionary<string, object?> { ["id"] = 42 });

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::Products::GetById:42", key);
    }

    [Fact]
    public void Generate_Mvc_WithCustomKey_OverridesSegment()
    {
        var context = BuildMvcContext("Users", "Get",
            new Dictionary<string, object?> { ["userId"] = 99 });

        var key = CacheKeyGenerator.Generate("UserInfo", context, "myapp::");

        Assert.Equal("myapp::UserInfo:99", key);
    }

    [Fact]
    public void Generate_Mvc_SkipsCancellationTokenArgs()
    {
        var context = BuildMvcContext("Orders", "List",
            new Dictionary<string, object?> { ["ct"] = CancellationToken.None });

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::Orders::List", key);
    }

    [Fact]
    public void Generate_Mvc_NullRouteController_FallsBackToUnknown()
    {
        var httpContext = new DefaultHttpContext();
        // no controller/action in route values
        var routeData = new RouteData();
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var execContext = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());

        var key = CacheKeyGenerator.Generate(null, execContext, "myapp::");

        Assert.Equal("myapp::Unknown::Unknown", key);
    }

    // ── Generate (EndpointFilterInvocationContext) ────────────────────────────

    [Fact]
    public void Generate_Endpoint_WithCustomKey_UsesCustomSegment()
    {
        var context = BuildEndpointContext(args: [42]);

        var key = CacheKeyGenerator.Generate("UserInfo", context, "myapp::");

        Assert.Equal("myapp::UserInfo:42", key);
    }

    [Fact]
    public void Generate_Endpoint_NoCustomKey_UsesEndpointName()
    {
        var context = BuildEndpointContext(endpointName: "GetProducts", args: []);

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::GetProducts", key);
    }

    [Fact]
    public void Generate_Endpoint_NoEndpointName_FallsBackToRequestPath()
    {
        var context = BuildEndpointContext(path: "/products/list", args: []);

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::products_list", key);
    }

    [Fact]
    public void Generate_Endpoint_SkipsHttpContextArgs()
    {
        var httpContext = new DefaultHttpContext();
        var context = BuildEndpointContext(endpointName: "GetUser", args: [httpContext, 99]);

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::GetUser:99", key);
    }

    [Fact]
    public void Generate_Endpoint_SkipsCancellationTokenArgs()
    {
        var context = BuildEndpointContext(endpointName: "List", args: [CancellationToken.None]);

        var key = CacheKeyGenerator.Generate(null, context, "myapp::");

        Assert.Equal("myapp::List", key);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EndpointFilterInvocationContext BuildEndpointContext(
        IEnumerable<object?>? args = null,
        string? endpointName = null,
        string? path = null)
    {
        var httpContext = new DefaultHttpContext();

        if (endpointName is not null)
        {
            var endpoint = new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new EndpointNameMetadata(endpointName)),
                endpointName);
            httpContext.SetEndpoint(endpoint);
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
        Dictionary<string, object?> args)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["controller"] = controller;
        httpContext.Request.RouteValues["action"] = action;
        var routeData = new RouteData(httpContext.Request.RouteValues);
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, new object());
    }
}
