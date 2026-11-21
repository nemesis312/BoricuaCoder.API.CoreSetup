using System.Text.Json;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BoricuaCoder.API.CoreSetup.Caching;

/// <summary>
/// Caches the response of an MVC action or a minimal API endpoint in Redis.
/// </summary>
/// <remarks>
/// For MVC controllers, apply directly as an attribute: <c>[Cache(300)]</c><br/>
/// For minimal APIs, chain on the route builder: <c>.AddEndpointFilter(new CacheAttribute(300))</c>
/// <para>
/// Only <c>GET</c> and <c>HEAD</c> requests are cached; every other method runs the endpoint without
/// touching the cache. Only successful responses are stored: for MVC an <see cref="ObjectResult"/> with a
/// non-null value and a status code of 200 (or none, as with <c>ActionResult&lt;T&gt;</c>); for minimal
/// APIs an <see cref="IValueHttpResult"/> with a non-null value and a status code of 200 (or none), or a
/// plain return value. On both paths <c>HttpContext.Response.StatusCode</c> must still be 200 after the
/// endpoint runs, and <c>string</c> and <see cref="ProblemDetails"/> values (including derived types such
/// as <c>ValidationProblemDetails</c>) are never cached. Nested results such as
/// <c>Results&lt;Ok&lt;T&gt;, NotFound&gt;</c> are unwrapped first, and only the value is stored. A hit is
/// replayed as 200 with <c>application/json</c>. A value that cannot be serialized (including a custom
/// converter that throws) is returned normally and not cached.
/// </para>
/// <para>
/// <see cref="UserScope"/> controls whether an entry is shared or kept per user. The default,
/// <see cref="CacheUserScope.Auto"/>, keys the entry per user when the request is authenticated or the
/// endpoint requires authorization. An authenticated caller without a <c>sub</c> or <c>NameIdentifier</c>
/// claim is never cached. Set <c>UserScope = CacheUserScope.Shared</c> only for responses that contain no
/// user-specific data: <c>[Cache(60, UserScope = CacheUserScope.Shared)]</c>.
/// </para>
/// <para>
/// When seconds is 0, the TTL falls back to <c>CoreSetup:Redis:DefaultTTL</c> (default 300 s).<br/>
/// When a custom key is supplied (e.g. "UserInfo"), the cache key becomes
/// <c>{PrefixKey}UserInfo:{args}:{endpointId}:{userSegment}</c>; otherwise it is
/// <c>{PrefixKey}{endpointId}:{args}:{userSegment}</c>, where the endpoint id is the controller/action name
/// (MVC) or the endpoint name or route (minimal APIs), each argument is escaped, or hashed when it is long
/// or complex, and the user segment is present only for per-user entries.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CacheAttribute : Attribute, IAsyncActionFilter, IEndpointFilter
{
    /// <summary>Cache TTL in seconds. 0 means use <c>CoreSetup:Redis:DefaultTTL</c>.</summary>
    public int Seconds { get; }

    /// <summary>Optional custom key segment. Overrides the auto-generated class::method segment.</summary>
    public string? CustomKey { get; }

    /// <summary>
    /// Whether the cached entry is shared by every caller or kept per user. Defaults to
    /// <see cref="CacheUserScope.Auto"/>.
    /// </summary>
    public CacheUserScope UserScope { get; set; } = CacheUserScope.Auto;

    /// <param name="seconds">Cache TTL in seconds. 0 falls back to DefaultTTL.</param>
    /// <param name="customKey">Custom key segment (e.g. "UserInfo"). Null uses reflection-based naming.</param>
    public CacheAttribute(int seconds = 0, string? customKey = null)
    {
        Seconds = seconds;
        CustomKey = customKey;
    }

    // ── MVC controller support ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!IsCacheableMethod(httpContext.Request.Method))
        {
            await next();
            return;
        }

        var cacheService = httpContext.RequestServices.GetService<ICacheService>();
        if (cacheService is null)
        {
            await next();
            return;
        }

        var scope = CacheUserScopeResolver.Resolve(httpContext, UserScope);
        if (!scope.CanCache)
        {
            await next();
            return;
        }

        var key = CacheKeyGenerator.Generate(CustomKey, context, scope.UserSegment);
        if (key is null)
        {
            await next();
            return;
        }

        var cached = await cacheService.GetAsync<string>(key);
        if (cached is not null)
        {
            context.Result = new ContentResult
            {
                Content = cached,
                ContentType = "application/json",
                StatusCode = StatusCodes.Status200OK
            };
            return;
        }

        var executed = await next();

        if (executed.Exception is null
            && executed.Result is ObjectResult { Value: not null and not string and not ProblemDetails } objectResult
            && objectResult.StatusCode is null or StatusCodes.Status200OK
            && httpContext.Response.StatusCode == StatusCodes.Status200OK)
        {
            var serializerOptions = httpContext.RequestServices
                .GetService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()?.Value?.JsonSerializerOptions
                ?? JsonSerializerOptions.Default;
            var json = TrySerialize(objectResult.Value, serializerOptions);
            if (json is not null)
                await cacheService.SetAsync(key, json, TimeSpan.FromSeconds(ResolveTtl(httpContext)));
        }
    }

    // ── Minimal API support ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!IsCacheableMethod(httpContext.Request.Method))
            return await next(context);

        var cacheService = httpContext.RequestServices.GetService<ICacheService>();
        if (cacheService is null)
            return await next(context);

        var scope = CacheUserScopeResolver.Resolve(httpContext, UserScope);
        if (!scope.CanCache)
            return await next(context);

        var key = CacheKeyGenerator.Generate(CustomKey, context, scope.UserSegment);
        if (key is null)
            return await next(context);

        var cached = await cacheService.GetAsync<string>(key);
        if (cached is not null)
            return Results.Text(cached, "application/json");

        var result = await next(context);

        var valueToCache = httpContext.Response.StatusCode == StatusCodes.Status200OK
            ? GetCacheableValue(result)
            : null;
        if (valueToCache is not null)
        {
            var serializerOptions = httpContext.RequestServices
                .GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?.Value?.SerializerOptions
                ?? JsonSerializerOptions.Default;
            var json = TrySerialize(valueToCache, serializerOptions);
            if (json is not null)
                await cacheService.SetAsync(key, json, TimeSpan.FromSeconds(ResolveTtl(httpContext)));
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsCacheableMethod(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

    private int ResolveTtl(HttpContext httpContext)
    {
        if (Seconds > 0)
            return Seconds;
        var redisOptions = httpContext.RequestServices.GetService<RedisOptions>() ?? new RedisOptions();
        return redisOptions.DefaultTTL;
    }

    /// <summary>
    /// Returns the value to store for a minimal API result, or null when it must not be cached.
    /// Nested results are unwrapped; only a 200 (or status-less) value result or a plain non-string
    /// value qualifies, and never a <see cref="ProblemDetails"/>.
    /// </summary>
    private static object? GetCacheableValue(object? result)
    {
        var current = result;
        while (current is INestedHttpResult nested)
            current = nested.Result;

        var value = current switch
        {
            null => null,
            IResult httpResult => httpResult is IValueHttpResult { Value: { } inner }
                                  && (httpResult as IStatusCodeHttpResult)?.StatusCode is null or StatusCodes.Status200OK
                ? inner
                : null,
            string => null,
            _ => current
        };

        return value is ProblemDetails ? null : value;
    }

    private static string? TrySerialize(object value, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Serialize(value, options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failing serializer or custom converter must never turn a good response into a 500.
            return null;
        }
    }
}
