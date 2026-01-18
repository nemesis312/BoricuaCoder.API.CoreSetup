using BoricuaCoder.API.CoreSetup.Options;
using BoricuaCoder.API.CoreSetup.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        // Bind options desde appsettings
        var options = configuration
            .GetSection(SectionName)
            .Get<CoreSetupOptions>() ?? new CoreSetupOptions();

        // JWT + Auth
        services.AddJwtAuthentication(options.Jwt);

        // Swagger con OAuth
        services.AddSwaggerWithOAuth(options.Swagger);

        // Si luego quieres exponer IOptions<CoreSetupOptions>
        services.Configure<CoreSetupOptions>(configuration.GetSection(SectionName));

        return services;
    }
}