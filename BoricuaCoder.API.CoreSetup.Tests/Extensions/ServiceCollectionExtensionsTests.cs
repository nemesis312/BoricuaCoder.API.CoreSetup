using BoricuaCoder.API.CoreSetup.Extensions;
using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BoricuaCoder.API.CoreSetup.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoreSetup_RegistersAuthenticationServices()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddCoreSetup(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var authSchemeProvider = serviceProvider.GetService<IAuthenticationSchemeProvider>();

        Assert.NotNull(authSchemeProvider);
    }

    [Fact]
    public void AddCoreSetup_RegistersCoreSetupOptions()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddCoreSetup(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<CoreSetupOptions>>();

        Assert.NotNull(options);
        Assert.NotNull(options.Value);
    }

    [Fact]
    public void AddCoreSetup_BindsJwtOptionsFromConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["CoreSetup:Jwt:Authority"] = "https://auth.example.com",
            ["CoreSetup:Jwt:Audience"] = "my-api",
            ["CoreSetup:Jwt:RequireHttpsMetadata"] = "false"
        });

        services.AddCoreSetup(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CoreSetupOptions>>().Value;

        Assert.Equal("https://auth.example.com", options.Jwt.Authority);
        Assert.Equal("my-api", options.Jwt.Audience);
        Assert.False(options.Jwt.RequireHttpsMetadata);
    }

    [Fact]
    public void AddCoreSetup_BindsSwaggerOptionsFromConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["CoreSetup:Swagger:Enabled"] = "true",
            ["CoreSetup:Swagger:Title"] = "My Custom API",
            ["CoreSetup:Swagger:Version"] = "v2",
            ["CoreSetup:Swagger:RoutePrefix"] = "api-docs"
        });

        services.AddCoreSetup(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CoreSetupOptions>>().Value;

        Assert.True(options.Swagger.Enabled);
        Assert.Equal("My Custom API", options.Swagger.Title);
        Assert.Equal("v2", options.Swagger.Version);
        Assert.Equal("api-docs", options.Swagger.RoutePrefix);
    }

    [Fact]
    public void AddCoreSetup_ReturnsServiceCollectionForChaining()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        var result = services.AddCoreSetup(configuration);

        Assert.Same(services, result);
    }

    [Fact]
    public void AddCoreSetup_WithEmptyConfiguration_UsesDefaults()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddCoreSetup(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CoreSetupOptions>>().Value;

        Assert.Equal(string.Empty, options.Jwt.Authority);
        Assert.Equal(string.Empty, options.Jwt.Audience);
        Assert.True(options.Jwt.RequireHttpsMetadata);
        Assert.True(options.Swagger.Enabled);
        Assert.Equal("API", options.Swagger.Title);
        Assert.Equal("v1", options.Swagger.Version);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? values = null)
    {
        var builder = new ConfigurationBuilder();

        if (values != null)
        {
            builder.AddInMemoryCollection(values);
        }
        else
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>());
        }

        return builder.Build();
    }
}
