using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
}
