using BoricuaCoder.API.CoreSetup.Caching;
using BoricuaCoder.API.CoreSetup.Options;
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ActionExecutingContext BuildMvcContext(
        ICacheService? cacheService,
        RedisOptions? redisOptions = null)
    {
        var services = new ServiceCollection();
        if (cacheService is not null)
        {
            services.AddSingleton(cacheService);
            services.AddSingleton(redisOptions ?? DefaultRedisOptions);
        }
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Request.RouteValues["controller"] = "Products";
        httpContext.Request.RouteValues["action"] = "GetAll";
        var routeData = new RouteData(httpContext.Request.RouteValues);
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
    }

    private static EndpointFilterInvocationContext BuildEndpointContext(
        ICacheService? cacheService,
        RedisOptions? redisOptions = null)
    {
        var services = new ServiceCollection();
        if (cacheService is not null)
        {
            services.AddSingleton(cacheService);
            services.AddSingleton(redisOptions ?? DefaultRedisOptions);
        }
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };

        var efContext = Substitute.For<EndpointFilterInvocationContext>();
        efContext.HttpContext.Returns(httpContext);
        efContext.Arguments.Returns(new List<object?>());
        return efContext;
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
