using Microsoft.AspNetCore.Authentication.JwtBearer;
using BoricuaCoder.API.CoreSetup.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration;

public class TokenValidationIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        _app = IntegrationTestBase.BuildApp();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public void AddCoreSetup_NoTokenValidationOverrides_DoesNotSetTokenValidationParameters()
    {
        var jwtOptions = _app!.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        // When no overrides are set the handler should use its own defaults,
        // so we should not have overridden ValidateIssuer/ValidateAudience to false.
        Assert.True(jwtOptions.TokenValidationParameters.ValidateIssuer);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateAudience);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateLifetime);
    }

    [Fact]
    public async Task AddCoreSetup_WithValidateIssuerFalse_AppliesOverride()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:TokenValidation:ValidateIssuer"] = "false"
        };

        await using var app = IntegrationTestBase.BuildApp(config);
        await app.StartAsync();

        var jwtOptions = app.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(jwtOptions.TokenValidationParameters.ValidateIssuer);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateAudience);
        Assert.True(jwtOptions.TokenValidationParameters.ValidateLifetime);
    }

    [Fact]
    public async Task AddCoreSetup_WithClockSkewZero_AppliesOverride()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:TokenValidation:ClockSkewSeconds"] = "0"
        };

        await using var app = IntegrationTestBase.BuildApp(config);
        await app.StartAsync();

        var jwtOptions = app.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.Zero, jwtOptions.TokenValidationParameters.ClockSkew);
    }

    [Fact]
    public async Task AddCoreSetup_WithClockSkewSeconds_AppliesCorrectTimeSpan()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:TokenValidation:ClockSkewSeconds"] = "120"
        };

        await using var app = IntegrationTestBase.BuildApp(config);
        await app.StartAsync();

        var jwtOptions = app.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.FromSeconds(120), jwtOptions.TokenValidationParameters.ClockSkew);
    }

    [Fact]
    public async Task AddCoreSetup_WithAllValidationDisabled_AppliesAllOverrides()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:TokenValidation:ValidateIssuer"] = "false",
            ["CoreSetup:Jwt:TokenValidation:ValidateAudience"] = "false",
            ["CoreSetup:Jwt:TokenValidation:ValidateLifetime"] = "false"
        };

        await using var app = IntegrationTestBase.BuildApp(config);
        await app.StartAsync();

        var jwtOptions = app.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(jwtOptions.TokenValidationParameters.ValidateIssuer);
        Assert.False(jwtOptions.TokenValidationParameters.ValidateAudience);
        Assert.False(jwtOptions.TokenValidationParameters.ValidateLifetime);
    }

    private static TokenValidationParameters GetParameters(WebApplication app) =>
        app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

    [Fact]
    public async Task ValidAlgorithms_WithoutOtherOverrides_AreApplied()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:ValidAlgorithms:0"] = "RS256"
        };

        await using var app = IntegrationTestBase.BuildApp(config);
        await app.StartAsync();

        Assert.Equal(["RS256"], GetParameters(app).ValidAlgorithms);
    }

    [Fact]
    public async Task ValidateIssuerOverride_KeepsValidAlgorithms()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:ValidAlgorithms:0"] = "RS256",
            ["CoreSetup:Jwt:ValidAlgorithms:1"] = "ES256",
            ["CoreSetup:Jwt:TokenValidation:ValidateIssuer"] = "false"
        };

        await using var app = IntegrationTestBase.BuildApp(config);
        await app.StartAsync();

        var tvp = GetParameters(app);
        Assert.False(tvp.ValidateIssuer);
        Assert.Equal(["RS256", "ES256"], tvp.ValidAlgorithms);
    }

    [Fact]
    public async Task ClockSkewOverride_DoesNotReplaceParametersInstance()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Jwt:TokenValidation:ClockSkewSeconds"] = "30"
        };

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(config);
        // Registered before AddCoreSetup, so it runs before the CoreSetup configure action.
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            o => o.TokenValidationParameters.NameClaimType = "preferred_username");
        builder.Services.AddCoreSetup(builder.Configuration);

        await using var app = builder.Build();
        app.UseCoreSetup();
        await app.StartAsync();

        var tvp = GetParameters(app);
        Assert.Equal(TimeSpan.FromSeconds(30), tvp.ClockSkew);
        Assert.Equal("preferred_username", tvp.NameClaimType);
    }
}
