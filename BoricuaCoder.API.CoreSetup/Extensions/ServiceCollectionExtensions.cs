using BoricuaCoder.API.CoreSetup.Options;
using BoricuaCoder.API.CoreSetup.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BoricuaCoder.API.CoreSetup.Extensions;

/// <summary>
/// Extension methods for configuring CoreSetup services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string SectionName = "CoreSetup";

    /// <summary>
    /// Adds JWT Bearer authentication and Swagger services configured from the "CoreSetup" section in appsettings.json.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCoreSetup(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(SectionName)
            .Get<CoreSetupOptions>() ?? new CoreSetupOptions();

        services.AddJwtAuthentication(options.Jwt);
        services.AddSwaggerWithOAuth(options.Swagger);

        services.AddOptions<CoreSetupOptions>()
            .Bind(configuration.GetSection(SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<CoreSetupOptions>, CoreSetupOptionsValidator>();

        return services;
    }
}