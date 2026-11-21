using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoricuaCoder.API.CoreSetup.Caching;

/// <summary>
/// Builds v2 cache keys. Layout (segments joined by <c>:</c>, an empty args part omitted):
/// <c>{endpointId}:{args...}:{scope}</c>, or with a custom key
/// <c>{customKey}:{args...}:{endpointId}:{scope}</c>. The Redis prefix is added by
/// <see cref="RedisCacheService"/>.
/// </summary>
/// <remarks>
/// The endpoint id is a readable name followed by <c>%i</c> and 8 hex characters that identify the
/// endpoint itself (controller type, method and signature, area, route pattern, hosts), so two endpoints
/// with the same readable name never share keys. The scope marker is always last and always present:
/// <c>%u</c> + 32 hex (per user), <c>%a</c> (anonymous per-user bucket) or <c>%s</c> (shared). Every
/// marker starts with <c>%</c>, which an escaped argument can only contain as <c>%25</c>, so an argument
/// can never forge an endpoint id or a scope.
/// </remarks>
internal static class CacheKeyGenerator
{
    private const int MaxKeyLength = 512;
    private const int MaxInlineStringLength = 64;
    private const string NullToken = "%";
    private const string EmptyToken = "%e";
    private const string HashMarker = "%h";
    private const string EndpointIdMarker = "%i";

    /// <summary>Builds a cache key from an MVC action filter context. Returns null when an argument cannot be encoded.</summary>
    internal static string? Generate(string? customKey, ActionExecutingContext context, string? userSegment)
    {
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "Unknown";
        var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
        var endpointId = $"{controller}::{action}{EndpointIdMarker}{MvcIdentity(context)}";

        var services = context.HttpContext.RequestServices;
        var isService = services?.GetService<IServiceProviderIsService>();
        var json = services?.GetService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()?.Value?.JsonSerializerOptions
                   ?? JsonSerializerOptions.Default;
        var loggerFactory = services?.GetService<ILoggerFactory>();

        var tokens = new List<string>();
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            context.ActionArguments.TryGetValue(parameter.Name, out var value);

            if (IsFrameworkType(parameter.ParameterType) || IsFrameworkValue(value)
                || IsMvcServiceParameter(parameter, isService))
                continue;

            var token = EncodeArg(value, json, loggerFactory);
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
        var endpoint = httpContext.GetEndpoint();
        var endpointId = Uri.EscapeDataString(ResolveEndpointId(httpContext)) + EndpointIdMarker + EndpointIdentity(endpoint);

        var services = httpContext.RequestServices;
        var isService = services?.GetService<IServiceProviderIsService>();
        var json = services?.GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?.Value?.SerializerOptions
                   ?? JsonSerializerOptions.Default;
        var loggerFactory = services?.GetService<ILoggerFactory>();

        // Declared handler parameters line up by position with the filter arguments. Without them
        // (no MethodInfo, or a count mismatch) fall back to the runtime type of each value.
        var arguments = context.Arguments;
        var declared = endpoint?.Metadata.GetMetadata<MethodInfo>()?.GetParameters();
        if (declared is not null && declared.Length != arguments.Count)
            declared = null;

        var tokens = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var value = arguments[i];
            if (IsFrameworkValue(value))
                continue;

            if (declared is not null)
            {
                var parameter = declared[i];
                if (IsFrameworkType(parameter.ParameterType)
                    || HasServiceAttribute(parameter)
                    || IsInjectableService(parameter.ParameterType, isService))
                    continue;
            }
            else if (value is not null && IsInjectableService(value.GetType(), isService))
            {
                continue;
            }

            var token = EncodeArg(value, json, loggerFactory);
            if (token is null)
                return null;
            tokens.Add(token);
        }

        return Compose(customKey, endpointId, tokens, userSegment);
    }

    /// <summary>
    /// Encodes one argument as a key-safe token. <c>%</c> is null, <c>%e</c> is an empty string, <c>%h…</c> is a hash; every other
    /// token is percent-escaped, so neither can be produced by a literal value. Returns null when the
    /// value cannot be serialized; that case is logged at Debug with the value's type name only.
    /// </summary>
    internal static string? EncodeArg(object? value, JsonSerializerOptions options, ILoggerFactory? loggerFactory = null)
    {
        switch (value)
        {
            case null:
                return NullToken;
            case string { Length: 0 }:
                return EmptyToken;
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
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Type name only: the value (and the exception message) can carry request data.
            loggerFactory?.CreateLogger(Extensions.ApplicationBuilderExtensions.LogCategory)
                .LogDebug("CoreSetup: [Cache] could not build a cache key for an argument of type {Type}; response not cached.",
                    type.FullName ?? type.Name);
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

    /// <summary>
    /// 8 hex characters identifying an MVC action: area, controller type, method name and parameter
    /// types. Without a <see cref="ControllerActionDescriptor"/> the descriptor's parameter types are used.
    /// </summary>
    private static string MvcIdentity(ActionExecutingContext context)
    {
        var area = context.RouteData.Values["area"]?.ToString() ?? string.Empty;
        var controllerType = string.Empty;
        var method = string.Empty;
        IEnumerable<Type> parameterTypes;

        if (context.ActionDescriptor is ControllerActionDescriptor { MethodInfo: { } methodInfo } descriptor)
        {
            controllerType = descriptor.ControllerTypeInfo?.FullName ?? string.Empty;
            method = methodInfo.Name;
            parameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType);
        }
        else
        {
            parameterTypes = context.ActionDescriptor.Parameters.Select(p => p.ParameterType);
        }

        return ShortHash(area, controllerType, method, JoinTypeNames(parameterTypes));
    }

    /// <summary>
    /// 8 hex characters identifying a minimal API endpoint: route pattern, handler method (when the
    /// metadata carries one) and the hosts it is restricted to.
    /// </summary>
    private static string EndpointIdentity(Endpoint? endpoint)
    {
        var rawText = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;

        var handler = string.Empty;
        if (endpoint?.Metadata.GetMetadata<MethodInfo>() is { } method)
            handler = string.Join("|",
                method.DeclaringType?.FullName ?? string.Empty,
                method.Name,
                JoinTypeNames(method.GetParameters().Select(p => p.ParameterType)));

        var hosts = endpoint?.Metadata.GetMetadata<IHostMetadata>()?.Hosts ?? [];
        var hostList = string.Join(",", hosts.Order(StringComparer.Ordinal));

        return ShortHash(rawText, handler, hostList);
    }

    private static string JoinTypeNames(IEnumerable<Type> types)
        => string.Join(",", types.Select(t => t.FullName ?? t.Name));

    private static string ShortHash(params string[] parts)
        => Sha256Hex(Encoding.UTF8.GetBytes(string.Join("\n", parts)))[..8];

    private static string? Normalize(string? value)
        => value?.Trim('/').Replace("/", "_");

    private static string Compose(string? customKey, string endpointId, List<string> tokens, string? userSegment)
    {
        // The scope marker is never omitted: a missing segment means a shared entry.
        userSegment = string.IsNullOrEmpty(userSegment) ? CacheUserScopeResolver.SharedSegment : userSegment;

        // The args part is never omitted when there is at least one token: every token is non-empty
        // (empty strings encode as %e), and the scope marker is always last.
        var args = tokens.Count > 0 ? string.Join(":", tokens) : null;
        var key = Join(customKey, endpointId, args, userSegment);
        if (key.Length <= MaxKeyLength)
            return key;

        if (args is not null)
        {
            args = HashMarker + Sha256Hex(Encoding.UTF8.GetBytes(args));
            key = Join(customKey, endpointId, args, userSegment);
            if (key.Length <= MaxKeyLength)
                return key;
        }

        // Still too long: hash the endpoint id first so a custom key stays usable for cascade deletes.
        endpointId = HashIfLong(endpointId);
        key = Join(customKey, endpointId, args, userSegment);
        if (key.Length <= MaxKeyLength || string.IsNullOrEmpty(customKey))
            return key;

        return Join(HashIfLong(customKey), endpointId, args, userSegment);
    }

    private static string HashIfLong(string value)
        => value.Length <= HashMarker.Length + 64
            ? value
            : HashMarker + Sha256Hex(Encoding.UTF8.GetBytes(value));

    private static string Join(string? customKey, string endpointId, string? args, string? userSegment)
    {
        string?[] parts = string.IsNullOrEmpty(customKey)
            ? [endpointId, args, userSegment]
            : [customKey, args, endpointId, userSegment];

        return string.Join(":", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static bool IsFrameworkValue(object? value)
        => value is not null && IsFrameworkType(value.GetType());

    private static bool IsFrameworkType(Type type)
        => type == typeof(CancellationToken)
           || typeof(HttpContext).IsAssignableFrom(type)
           || typeof(HttpRequest).IsAssignableFrom(type)
           || typeof(HttpResponse).IsAssignableFrom(type)
           || typeof(ClaimsPrincipal).IsAssignableFrom(type);

    private static bool IsMvcServiceParameter(ParameterDescriptor parameter, IServiceProviderIsService? isService)
    {
        var source = parameter.BindingInfo?.BindingSource;
        if (source == BindingSource.Services)
            return true;

        if (parameter is IParameterInfoParameterDescriptor { ParameterInfo: { } info } && HasServiceAttribute(info))
            return true;

        // An explicit request binding source (Query, Path, Body, Form, Header, Custom...) is request
        // data even when the type also happens to be registered in DI.
        if (source is not null)
            return false;

        return IsInjectableService(parameter.ParameterType, isService);
    }

    private static bool HasServiceAttribute(ParameterInfo parameter)
        => parameter.IsDefined(typeof(FromKeyedServicesAttribute), inherit: true)
           || parameter.GetCustomAttributes(inherit: true).OfType<IFromServiceMetadata>().Any();

    /// <summary>
    /// True when DI can resolve the type and it is not request data. MS DI reports every closed
    /// <c>IEnumerable&lt;T&gt;</c> as a service, so collections never count as services here.
    /// </summary>
    private static bool IsInjectableService(Type type, IServiceProviderIsService? isService)
    {
        if (isService is null || type.IsValueType || type == typeof(string) || type == typeof(object))
            return false;

        if (typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        return isService.IsService(type);
    }

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
