using System.Security.Claims;
using System.Text.Json;
using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BoricuaCoder.API.CoreSetup.Tests.Caching;

public class CacheAttributeTests
{
    private static readonly RedisOptions DefaultRedisOptions = new()
    {
        PrefixKey = "test::",
        DefaultTTL = 300
    };

    // ── IAsyncActionFilter (MVC) ──────────────────────────────────────────────

    [Fact]
    public async Task OnActionExecutionAsync_CacheMiss_CallsNextAndStoresResult()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildMvcContext(cacheService);
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, BuildNext(new OkObjectResult(new { Name = "Widget" })));

        await cacheService.Received(1)
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task OnActionExecutionAsync_CacheHit_SetsContentResultAndSkipsNext()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns("{\"Name\":\"Widget\"}");

        var context = BuildMvcContext(cacheService);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });

        Assert.False(nextCalled);
        var result = Assert.IsType<ContentResult>(context.Result);
        Assert.Equal("{\"Name\":\"Widget\"}", result.Content);
        Assert.Equal("application/json", result.ContentType);
    }

    [Fact]
    public async Task OnActionExecutionAsync_NoCacheService_CallsNextWithoutCaching()
    {
        var context = BuildMvcContext(cacheService: null);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_NonOkResult_DoesNotCache()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildMvcContext(cacheService);
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, BuildNext(new NotFoundResult()));

        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task OnActionExecutionAsync_ZeroSeconds_UsesFallbackTTL()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildMvcContext(cacheService, redisOptions: new RedisOptions { DefaultTTL = 120 });
        var attribute = new CacheAttribute(0);

        await attribute.OnActionExecutionAsync(context, BuildNext(new OkObjectResult("data")));

        await cacheService.Received(1)
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task OnActionExecutionAsync_WithCustomKey_PassesCustomKeyToGenerator()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildMvcContext(cacheService);
        var attribute = new CacheAttribute(60, "UserInfo");
        string? capturedKey = null;

        cacheService.When(s => s.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()))
            .Do(call => capturedKey = call.ArgAt<string>(0));

        await attribute.OnActionExecutionAsync(context, BuildNext(new OkObjectResult("data")));

        Assert.NotNull(capturedKey);
        Assert.StartsWith("UserInfo", capturedKey);
    }

    // ── IEndpointFilter (Minimal APIs) ────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_CacheMiss_CallsNextAndStoresResult()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);
        var nextResult = Results.Ok(new { Name = "Widget" });

        var result = await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(nextResult));

        Assert.Same(nextResult, result);
        await cacheService.Received(1)
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task InvokeAsync_CacheHit_ReturnsTextResultWithoutCallingNext()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns("{\"Name\":\"Widget\"}");

        var context = BuildEndpointContext(cacheService);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        var result = await attribute.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        Assert.False(nextCalled);
        Assert.NotNull(result);
        // Result should be a text/json response wrapping the cached string
        Assert.IsAssignableFrom<IResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_NoCacheService_CallsNextWithoutCaching()
    {
        var context = BuildEndpointContext(cacheService: null);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_NullResult_DoesNotCache()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);

        await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_ZeroSeconds_UsesFallbackTTL()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);

        var context = BuildEndpointContext(cacheService, redisOptions: new RedisOptions { DefaultTTL = 90 });
        var attribute = new CacheAttribute(0);

        await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok("data")));

        await cacheService.Received(1)
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), TimeSpan.FromSeconds(90));
    }

    // ── Unencodable arguments ─────────────────────────────────────────────────

    private sealed class Node
    {
        public Node? Next { get; set; }
    }

    private static Node SelfReferencingNode()
    {
        var node = new Node();
        node.Next = node;
        return node;
    }

    [Fact]
    public async Task OnActionExecutionAsync_UnencodableArgument_CallsNextWithoutTouchingCache()
    {
        var cacheService = Substitute.For<ICacheService>();
        var context = BuildMvcContext(cacheService, args: [("filter", SelfReferencingNode())]);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()) { Result = new OkObjectResult("data") });
        });

        Assert.True(nextCalled);
        Assert.Empty(cacheService.ReceivedCalls());
    }

    [Fact]
    public async Task InvokeAsync_UnencodableArgument_CallsNextWithoutTouchingCache()
    {
        var cacheService = Substitute.For<ICacheService>();
        var context = BuildEndpointContext(cacheService, args: [SelfReferencingNode()]);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok("data"));
        });

        Assert.True(nextCalled);
        Assert.Empty(cacheService.ReceivedCalls());
    }

    // ── Method filter, user scope and response filtering ─────────────────────

    public sealed record Item(int Id, string Name);

    private static ClaimsPrincipal User(string? sub)
    {
        var claims = sub is null ? [] : new[] { new Claim("sub", sub) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static ICacheService EmptyCache()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns((string?)null);
        return cacheService;
    }

    private static string? CapturedGetKey(ICacheService cacheService)
        => cacheService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ICacheService.GetAsync))
            .Select(c => (string)c.GetArguments()[0]!)
            .SingleOrDefault();

    [Fact]
    public async Task OnActionExecutionAsync_Post_BypassesCache()
    {
        var cacheService = EmptyCache();
        var context = BuildMvcContext(cacheService, method: HttpMethods.Post);
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()) { Result = new OkObjectResult("data") });
        });

        Assert.True(nextCalled);
        await cacheService.DidNotReceive().GetAsync<string>(Arg.Any<string>());
        Assert.Empty(cacheService.ReceivedCalls());
    }

    [Fact]
    public async Task OnActionExecutionAsync_Head_UsesCache()
    {
        var cacheService = Substitute.For<ICacheService>();
        cacheService.GetAsync<string>(Arg.Any<string>()).Returns("{\"Name\":\"Widget\"}");
        var context = BuildMvcContext(cacheService, method: HttpMethods.Head);
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, BuildNext(new OkObjectResult("data")));

        var result = Assert.IsType<ContentResult>(context.Result);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ObjectResultWithNullStatus_IsCached()
    {
        // ActionResult<T> returning a value becomes an ObjectResult with a null StatusCode.
        var cacheService = EmptyCache();
        var context = BuildMvcContext(cacheService);
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, BuildNext(new ObjectResult(new Item(1, "Widget"))));

        await cacheService.Received(1)
            .SetAsync(Arg.Any<string>(), JsonSerializer.Serialize(new Item(1, "Widget")), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task OnActionExecutionAsync_ObjectResult404_IsNotCached()
    {
        var cacheService = EmptyCache();
        var context = BuildMvcContext(cacheService);
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context,
            BuildNext(new ObjectResult(new { Error = "missing" }) { StatusCode = StatusCodes.Status404NotFound }));

        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task OnActionExecutionAsync_TwoUsers_UseDifferentKeys()
    {
        var cacheA = EmptyCache();
        var cacheB = EmptyCache();
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(BuildMvcContext(cacheA, user: User("a")), BuildNext(new OkObjectResult("data")));
        await attribute.OnActionExecutionAsync(BuildMvcContext(cacheB, user: User("b")), BuildNext(new OkObjectResult("data")));

        var keyA = CapturedGetKey(cacheA);
        var keyB = CapturedGetKey(cacheB);
        Assert.NotNull(keyA);
        Assert.NotNull(keyB);
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public async Task OnActionExecutionAsync_SharedScope_TwoUsers_SameKey()
    {
        var cacheA = EmptyCache();
        var cacheB = EmptyCache();
        var attribute = new CacheAttribute(60) { UserScope = CacheUserScope.Shared };

        await attribute.OnActionExecutionAsync(BuildMvcContext(cacheA, user: User("a")), BuildNext(new OkObjectResult("data")));
        await attribute.OnActionExecutionAsync(BuildMvcContext(cacheB, user: User("b")), BuildNext(new OkObjectResult("data")));

        var keyA = CapturedGetKey(cacheA);
        Assert.NotNull(keyA);
        Assert.Equal(keyA, CapturedGetKey(cacheB));
    }

    [Fact]
    public async Task OnActionExecutionAsync_AuthenticatedWithoutSub_NeverTouchesCache()
    {
        var cacheService = EmptyCache();
        var context = BuildMvcContext(cacheService, user: User(sub: null));
        var nextCalled = false;
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()) { Result = new OkObjectResult("data") });
        });

        Assert.True(nextCalled);
        Assert.Empty(cacheService.ReceivedCalls());
    }

    [Fact]
    public async Task OnActionExecutionAsync_AuthorizeMetadataAnonymous_UsesAnonymousSegment()
    {
        var cacheService = EmptyCache();
        var context = BuildMvcContext(cacheService, metadata: [new AuthorizeAttribute()]);
        var attribute = new CacheAttribute(60);

        await attribute.OnActionExecutionAsync(context, BuildNext(new OkObjectResult("data")));

        Assert.EndsWith(":u-anon", CapturedGetKey(cacheService));
    }

    [Fact]
    public async Task InvokeAsync_ForbidResult_IsNotCached()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);
        var forbid = Results.Forbid();

        var result = await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(forbid));

        Assert.Same(forbid, result);
        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_NotFound_IsNotCached()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);

        // NotFound<T> carries a value, so this exercises the status code check.
        await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(TypedResults.NotFound(new Item(1, "Widget"))));

        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_NestedResultsNotFound_DoesNotCache()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);
        Results<Ok<Item>, NotFound> nested = TypedResults.NotFound();

        var result = await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(nested));

        Assert.Same(nested, result);
        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_NestedResultsOk_CachesOnlyValue()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);
        var item = new Item(7, "Widget");
        Results<Ok<Item>, NotFound> nested = TypedResults.Ok(item);
        string? stored = null;
        cacheService.When(s => s.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()))
            .Do(call => stored = call.ArgAt<string>(1));

        var result = await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(nested));

        Assert.Same(nested, result);
        Assert.Equal(JsonSerializer.Serialize(item), stored);
        Assert.DoesNotContain("result", stored!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_Created201_IsNotCached()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);

        await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(TypedResults.Created("/items/1", new Item(1, "Widget"))));

        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_RawString_IsNotCached()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);

        var result = await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>("plain text"));

        Assert.Equal("plain text", result);
        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_UnserializableValue_ReturnsResultWithoutCaching()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService);
        var attribute = new CacheAttribute(60);
        var ok = TypedResults.Ok(SelfReferencingNode());

        var result = await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(ok));

        Assert.Same(ok, result);
        await cacheService.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InvokeAsync_Post_BypassesCache()
    {
        var cacheService = EmptyCache();
        var context = BuildEndpointContext(cacheService, method: HttpMethods.Post);
        var attribute = new CacheAttribute(60);

        await attribute.InvokeAsync(context, _ => ValueTask.FromResult<object?>(TypedResults.Ok(new Item(1, "Widget"))));

        Assert.Empty(cacheService.ReceivedCalls());
    }

    [Fact]
    public async Task InvokeAsync_TwoUsers_UseDifferentKeys()
    {
        var cacheA = EmptyCache();
        var cacheB = EmptyCache();
        var attribute = new CacheAttribute(60);

        await attribute.InvokeAsync(BuildEndpointContext(cacheA, user: User("a")), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));
        await attribute.InvokeAsync(BuildEndpointContext(cacheB, user: User("b")), _ => ValueTask.FromResult<object?>(TypedResults.Ok("x")));

        Assert.NotEqual(CapturedGetKey(cacheA), CapturedGetKey(cacheB));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ActionExecutingContext BuildMvcContext(
        ICacheService? cacheService,
        RedisOptions? redisOptions = null,
        (string Name, object? Value)[]? args = null,
        string method = "GET",
        ClaimsPrincipal? user = null,
        object[]? metadata = null)
    {
        var services = new ServiceCollection();
        if (cacheService is not null)
        {
            services.AddSingleton(cacheService);
            services.AddSingleton(redisOptions ?? DefaultRedisOptions);
        }
        var sp = services.BuildServiceProvider();

        var httpContext = BuildHttpContext(sp, method, user, metadata);
        httpContext.Request.RouteValues["controller"] = "Products";
        httpContext.Request.RouteValues["action"] = "GetAll";
        var routeData = new RouteData(httpContext.Request.RouteValues);
        args ??= [];
        var descriptor = new ActionDescriptor
        {
            Parameters = args
                .Select(a => new ParameterDescriptor { Name = a.Name, ParameterType = a.Value?.GetType() ?? typeof(object) })
                .ToList()
        };
        var actionContext = new ActionContext(httpContext, routeData, descriptor);
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args.ToDictionary(a => a.Name, a => a.Value), new object());
    }

    private static EndpointFilterInvocationContext BuildEndpointContext(
        ICacheService? cacheService,
        RedisOptions? redisOptions = null,
        object?[]? args = null,
        string method = "GET",
        ClaimsPrincipal? user = null,
        object[]? metadata = null)
    {
        var services = new ServiceCollection();
        if (cacheService is not null)
        {
            services.AddSingleton(cacheService);
            services.AddSingleton(redisOptions ?? DefaultRedisOptions);
        }
        var sp = services.BuildServiceProvider();

        var httpContext = BuildHttpContext(sp, method, user, metadata);

        var efContext = Substitute.For<EndpointFilterInvocationContext>();
        efContext.HttpContext.Returns(httpContext);
        efContext.Arguments.Returns(new List<object?>(args ?? []));
        return efContext;
    }

    private static DefaultHttpContext BuildHttpContext(
        IServiceProvider sp, string method, ClaimsPrincipal? user, object[]? metadata)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Request.Method = method;
        if (user is not null)
            httpContext.User = user;
        if (metadata is not null)
            httpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
        return httpContext;
    }

    private static ActionExecutionDelegate BuildNext(IActionResult actionResult)
    {
        return () =>
        {
            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var executedContext = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object())
            {
                Result = actionResult
            };
            return Task.FromResult(executedContext);
        };
    }
}
