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
/// When seconds is 0, the TTL falls back to <c>CoreSetup:Redis:DefaultTTL</c> (default 300 s).<br/>
/// When a custom key is supplied (e.g. "UserInfo"), the cache key becomes
/// <c>{PrefixKey}UserInfo:{args}</c>; otherwise the key is derived from the controller/action name
/// (MVC) or endpoint name (minimal APIs) plus the serialized arguments.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CacheAttribute : Attribute, IAsyncActionFilter, IEndpointFilter
{
    /// <summary>Cache TTL in seconds. 0 means use <c>CoreSetup:Redis:DefaultTTL</c>.</summary>
    public int Seconds { get; }

    /// <summary>Optional custom key segment. Overrides the auto-generated class::method segment.</summary>
    public string? CustomKey { get; }

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
        var cacheService = context.HttpContext.RequestServices.GetService<ICacheService>();
        if (cacheService is null)
        {
            await next();
            return;
        }

        var redisOptions = context.HttpContext.RequestServices.GetService<RedisOptions>() ?? new RedisOptions();
        var ttl = Seconds > 0 ? Seconds : redisOptions.DefaultTTL;
        var key = CacheKeyGenerator.Generate(CustomKey, context, "");

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

        if (executed.Result is OkObjectResult { Value: not null } okResult)
        {
            var mvcJsonOptions = context.HttpContext.RequestServices
                .GetService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>();
            var serializerOptions = mvcJsonOptions?.Value?.JsonSerializerOptions ?? JsonSerializerOptions.Default;
            var json = JsonSerializer.Serialize(okResult.Value, serializerOptions);
            await cacheService.SetAsync(key, json, TimeSpan.FromSeconds(ttl));
        }
    }

    // ── Minimal API support ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var cacheService = context.HttpContext.RequestServices.GetService<ICacheService>();
        if (cacheService is null)
            return await next(context);

        var redisOptions = context.HttpContext.RequestServices.GetService<RedisOptions>() ?? new RedisOptions();
        var ttl = Seconds > 0 ? Seconds : redisOptions.DefaultTTL;
        var key = CacheKeyGenerator.Generate(CustomKey, context, "");

        var cached = await cacheService.GetAsync<string>(key);
        if (cached is not null)
            return Results.Text(cached, "application/json");

        var result = await next(context);

        if (result is not null)
        {
            // Extract the underlying value when the result wraps it (IValueHttpResult)
            var valueToCache = result is IValueHttpResult valueResult ? valueResult.Value : result;
            if (valueToCache is not null)
            {
                var httpJsonOptions = context.HttpContext.RequestServices
                    .GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
                var serializerOptions = httpJsonOptions?.Value?.SerializerOptions ?? JsonSerializerOptions.Default;
                var json = JsonSerializer.Serialize(valueToCache, serializerOptions);
                await cacheService.SetAsync(key, json, TimeSpan.FromSeconds(ttl));
            }
        }

        return result;
    }
}
