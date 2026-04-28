using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using CoreSetupExtensions = BoricuaCoder.API.CoreSetup.Extensions.ApplicationBuilderExtensions;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration;

public class LoggingIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private TestLogCollector _collector = null!;

    public async Task InitializeAsync()
    {
        _app = IntegrationTestBase.BuildAppWithTestLogging(out _collector);
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public void UseCoreSetup_EmitsDebugLog_ForJwtConfiguration()
    {
        var jwtLogs = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory
                     && r.Level == LogLevel.Debug
                     && r.Message.Contains("JWT authentication configured"))
            .ToList();

        Assert.Single(jwtLogs);
    }

    [Fact]
    public void UseCoreSetup_EmitsDebugLog_ForSwaggerConfiguration()
    {
        var swaggerLogs = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory
                     && r.Level == LogLevel.Debug
                     && r.Message.Contains("Swagger UI enabled"))
            .ToList();

        Assert.Single(swaggerLogs);
    }

    [Fact]
    public void UseCoreSetup_RequireHttpsMetadataFalse_EmitsWarning()
    {
        var warnings = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory
                     && r.Level == LogLevel.Warning
                     && r.Message.Contains("RequireHttpsMetadata is disabled"))
            .ToList();

        Assert.Single(warnings);
    }

    [Fact]
    public void UseCoreSetup_WithOAuth_EmitsDebugLog_ForOAuthConfiguration()
    {
        var oauthLogs = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory
                     && r.Level == LogLevel.Debug
                     && r.Message.Contains("OAuth2 configured"))
            .ToList();

        Assert.Single(oauthLogs);
    }
}

public class LoggingSwaggerDisabledTests : IAsyncLifetime
{
    private WebApplication? _app;
    private TestLogCollector _collector = null!;

    public async Task InitializeAsync()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Swagger:Enabled"] = "false"
        };

        _app = IntegrationTestBase.BuildAppWithTestLogging(out _collector, config);
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public void UseCoreSetup_SwaggerDisabled_EmitsDebugLog()
    {
        var logs = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory
                     && r.Level == LogLevel.Debug
                     && r.Message.Contains("Swagger UI is disabled"))
            .ToList();

        Assert.Single(logs);
    }

    [Fact]
    public void UseCoreSetup_SwaggerDisabled_DoesNotEmitSwaggerEnabledLog()
    {
        var logs = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory
                     && r.Message.Contains("Swagger UI enabled"))
            .ToList();

        Assert.Empty(logs);
    }
}
