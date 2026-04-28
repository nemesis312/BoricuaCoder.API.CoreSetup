using Microsoft.Extensions.Options;

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

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
