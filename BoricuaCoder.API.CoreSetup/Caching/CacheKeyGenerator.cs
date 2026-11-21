using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BoricuaCoder.API.CoreSetup.Caching;

/// <summary>
/// Builds v2 cache keys. Layout (segments joined by <c>:</c>, empty parts omitted):
/// <c>{endpointId}:{args...}:{userSegment}</c>, or with a custom key
/// <c>{customKey}:{args...}:{endpointId}:{userSegment}</c>. The Redis prefix is added by
/// <see cref="RedisCacheService"/>.
/// </summary>
internal static class CacheKeyGenerator
{
    private const int MaxKeyLength = 512;
    private const int MaxInlineStringLength = 64;
    private const string NullToken = "%";
    private const string HashMarker = "%h";

    /// <summary>Builds a cache key from an MVC action filter context. Returns null when an argument cannot be encoded.</summary>
    internal static string? Generate(string? customKey, ActionExecutingContext context, string? userSegment)
    {
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "Unknown";
        var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
        var endpointId = $"{controller}::{action}";

        var services = context.HttpContext.RequestServices;
        var isService = services?.GetService<IServiceProviderIsService>();
        var json = services?.GetService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()?.Value?.JsonSerializerOptions
                   ?? JsonSerializerOptions.Default;

        var tokens = new List<string>();
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            context.ActionArguments.TryGetValue(parameter.Name, out var value);

            if (IsSkippedType(parameter.ParameterType, isService) || IsSkippedValue(value, isService))
                continue;

            var token = EncodeArg(value, json);
            if (token is null)
                return null;
            tokens.Add(token);
        }

        return Compose(customKey, endpointId, tokens, userSegment);
    }

    /// <summary>Builds a cache key from a minimal API endpoint filter context. Returns null when an argument cannot be encoded.</summary>
    internal static string? Generate(string? customKey, EndpointFilterInvocationContext context, string? userSegment)
    {
        var httpContext = context.HttpContext;
        var endpointId = Uri.EscapeDataString(ResolveEndpointId(httpContext));

        var services = httpContext.RequestServices;
        var isService = services?.GetService<IServiceProviderIsService>();
        var json = services?.GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?.Value?.SerializerOptions
                   ?? JsonSerializerOptions.Default;

        var tokens = new List<string>();
        foreach (var value in context.Arguments)
        {
            if (IsSkippedValue(value, isService))
                continue;

            var token = EncodeArg(value, json);
            if (token is null)
                return null;
            tokens.Add(token);
        }

        return Compose(customKey, endpointId, tokens, userSegment);
    }

    /// <summary>
    /// Encodes one argument as a key-safe token. <c>%</c> is null, <c>%h…</c> is a hash; every other
    /// token is percent-escaped, so neither can be produced by a literal value. Returns null when the
    /// value cannot be serialized.
    /// </summary>
    internal static string? EncodeArg(object? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                return NullToken;
            case string s:
                return s.Length <= MaxInlineStringLength
                    ? Uri.EscapeDataString(s)
                    : HashMarker + Sha256Hex(Encoding.UTF8.GetBytes(s))[..32];
            case DateTime dt:
                return Uri.EscapeDataString(dt.ToString("O", CultureInfo.InvariantCulture));
            case DateTimeOffset dto:
                return Uri.EscapeDataString(dto.ToString("O", CultureInfo.InvariantCulture));
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal or Guid)
            return Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type, options);
            return HashMarker + Sha256Hex(bytes)[..32];
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ResolveEndpointId(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();

        var name = endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        if (!string.IsNullOrEmpty(name))
            return name;

        var pattern = Normalize((endpoint as RouteEndpoint)?.RoutePattern.RawText);
        if (!string.IsNullOrEmpty(pattern))
            return pattern;

        var path = Normalize(httpContext.Request.Path.Value);
        if (!string.IsNullOrEmpty(path))
            return path;

        return "unknown";
    }

    private static string? Normalize(string? value)
        => value?.Trim('/').Replace("/", "_");

    private static string Compose(string? customKey, string endpointId, List<string> tokens, string? userSegment)
    {
        var args = string.Join(":", tokens);
        var key = Join(customKey, endpointId, tokens.Count > 0 ? args : null, userSegment);

        if (key.Length <= MaxKeyLength)
            return key;

        var hashedArgs = HashMarker + Sha256Hex(Encoding.UTF8.GetBytes(args));
        return Join(customKey, endpointId, hashedArgs, userSegment);
    }

    private static string Join(string? customKey, string endpointId, string? args, string? userSegment)
    {
        string?[] parts = string.IsNullOrEmpty(customKey)
            ? [endpointId, args, userSegment]
            : [customKey, args, endpointId, userSegment];

        return string.Join(":", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static bool IsSkippedValue(object? value, IServiceProviderIsService? isService)
        => value is not null && IsSkippedType(value.GetType(), isService);

    private static bool IsSkippedType(Type type, IServiceProviderIsService? isService)
    {
        if (type == typeof(CancellationToken)
            || typeof(HttpContext).IsAssignableFrom(type)
            || typeof(HttpRequest).IsAssignableFrom(type)
            || typeof(HttpResponse).IsAssignableFrom(type)
            || typeof(ClaimsPrincipal).IsAssignableFrom(type))
            return true;

        // Value types and strings are request data, never injected services.
        if (type.IsValueType || type == typeof(string) || type == typeof(object))
            return false;

        return isService?.IsService(type) == true;
    }

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
