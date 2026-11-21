using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BoricuaCoder.API.CoreSetup.Options;

/// <summary>
/// Validates <see cref="CoreSetupOptions"/> at application startup.
/// </summary>
internal sealed class CoreSetupOptionsValidator : IValidateOptions<CoreSetupOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CoreSetupOptions options)
    {
        var errors = new List<string>();

        if (!string.IsNullOrEmpty(options.Jwt.Authority) &&
            !Uri.TryCreate(options.Jwt.Authority, UriKind.Absolute, out _))
        {
            errors.Add(
                $"CoreSetup:Jwt:Authority must be a valid absolute URI. Got: '{options.Jwt.Authority}'");
        }

        if (options.Swagger.Enabled)
        {
            if (!string.IsNullOrEmpty(options.Swagger.OAuth.AuthorizationUrl) &&
                !Uri.TryCreate(options.Swagger.OAuth.AuthorizationUrl, UriKind.Absolute, out _))
            {
                errors.Add(
                    $"CoreSetup:Swagger:OAuth:AuthorizationUrl must be a valid absolute URI. Got: '{options.Swagger.OAuth.AuthorizationUrl}'");
            }

            if (!string.IsNullOrEmpty(options.Swagger.OAuth.TokenUrl) &&
                !Uri.TryCreate(options.Swagger.OAuth.TokenUrl, UriKind.Absolute, out _))
            {
                errors.Add(
                    $"CoreSetup:Swagger:OAuth:TokenUrl must be a valid absolute URI. Got: '{options.Swagger.OAuth.TokenUrl}'");
            }

            var hasAuthUrl = !string.IsNullOrEmpty(options.Swagger.OAuth.AuthorizationUrl);
            var hasTokenUrl = !string.IsNullOrEmpty(options.Swagger.OAuth.TokenUrl);

            if (hasAuthUrl && !hasTokenUrl)
                errors.Add("CoreSetup:Swagger:OAuth:TokenUrl is required when AuthorizationUrl is set.");

            if (hasTokenUrl && !hasAuthUrl)
                errors.Add("CoreSetup:Swagger:OAuth:AuthorizationUrl is required when TokenUrl is set.");
        }

        if (options.Redis.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Redis.ConnectionString))
                errors.Add("CoreSetup:Redis:ConnectionString is required when Redis is enabled.");
            else
                ValidateRedisConnectionString(options.Redis.ConnectionString, errors);

            if (options.Redis.DefaultTTL <= 0)
                errors.Add("CoreSetup:Redis:DefaultTTL must be greater than 0.");

            if (options.Redis.ShortCircuit < 0)
                errors.Add("CoreSetup:Redis:ShortCircuit must be 0 or greater.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Checks that the connection string parses and names an endpoint. Messages never include the
    /// value, which can carry a password.
    /// </summary>
    private static void ValidateRedisConnectionString(string connectionString, List<string> errors)
    {
        var trimmed = connectionString.AsSpan().TrimStart();
        if (trimmed.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "CoreSetup:Redis:ConnectionString uses URI format (redis://), which StackExchange.Redis does not support. Use \"host:port,password=...,ssl=true\".");
            return;
        }

        ConfigurationOptions parsed;
        try
        {
            parsed = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.Add("CoreSetup:Redis:ConnectionString is not a valid StackExchange.Redis configuration string.");
            return;
        }

        if (parsed.EndPoints.Count == 0)
            errors.Add("CoreSetup:Redis:ConnectionString must contain at least one host:port endpoint.");
    }
}
