using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace BoricuaCoder.API.CoreSetup.Caching;

internal static class CacheKeyGenerator
{
    private const int MaxKeyLength = 512;

    /// <summary>Builds a cache key from an MVC action filter context.</summary>
    internal static string Generate(string? customKey, ActionExecutingContext context, string prefix)
    {
        string segment;
        if (customKey is not null)
        {
            segment = customKey;
        }
        else
        {
            var controller = context.RouteData.Values["controller"]?.ToString() ?? "Unknown";
            var action = context.RouteData.Values["action"]?.ToString() ?? "Unknown";
            segment = $"{controller}::{action}";
        }

        var args = BuildArgsSegment(context.ActionArguments.Values
            .Where(v => v is not null and not CancellationToken)
            .Select(v => v!.ToString() ?? string.Empty));

        return BuildKey(prefix, segment, args);
    }

    /// <summary>Builds a cache key from a minimal API endpoint filter context.</summary>
    internal static string Generate(string? customKey, EndpointFilterInvocationContext context, string prefix)
    {
        string segment;
        if (customKey is not null)
        {
            segment = customKey;
        }
        else
        {
            var endpointName = context.HttpContext.GetEndpoint()
                ?.Metadata.GetMetadata<IEndpointNameMetadata>()
                ?.EndpointName;

            segment = endpointName
                      ?? context.HttpContext.Request.Path.Value?.TrimStart('/').Replace("/", "_")
                      ?? "unknown";
        }

        var args = BuildArgsSegment(context.Arguments
            .Where(a => a is not null and not CancellationToken and not HttpContext)
            .Select(a => a!.ToString() ?? string.Empty));

        return BuildKey(prefix, segment, args);
    }

    internal static string BuildArgsSegment(IEnumerable<string> parts)
        => string.Join(":", parts.Where(p => !string.IsNullOrEmpty(p)));

    internal static string BuildKey(string prefix, string segment, string args)
    {
        var key = string.IsNullOrEmpty(args)
            ? $"{prefix}{segment}"
            : $"{prefix}{segment}:{args}";

        if (key.Length <= MaxKeyLength)
            return key;

        // Hash the args portion to stay within Redis key size limits
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return $"{prefix}{segment}:{hash}";
    }
}
