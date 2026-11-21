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

    // ── Redis startup warnings (connection is lazy, so no server is needed) ──

    private const string EmptyPrefixWarning =
        "CoreSetup: Redis PrefixKey is empty. Cache keys share the database namespace with other apps and DeleteAllAsync is disabled. Set CoreSetup:Redis:PrefixKey.";

    private const string InsecureRemoteWarning =
        "CoreSetup: Redis connection to a non-loopback host does not use TLS (ssl=true). Credentials and cached responses travel in plain text.";

    [Fact]
    public void UseCoreSetup_EmptyPrefixKey_LogsWarning()
    {
        var warnings = RedisWarnings("localhost:6379", prefixKey: "");

        Assert.Single(warnings, m => m == EmptyPrefixWarning);
    }

    [Fact]
    public void UseCoreSetup_RemoteRedisWithoutSsl_LogsWarning()
    {
        const string connectionString = "redis.example.com:6379,password=s3cret";

        var warnings = RedisWarnings(connectionString, prefixKey: "app::");

        Assert.Single(warnings, m => m == InsecureRemoteWarning);
        Assert.DoesNotContain(warnings, m => m.Contains("s3cret") || m.Contains("redis.example.com"));
        Assert.DoesNotContain(warnings, m => m == EmptyPrefixWarning);
    }

    [Fact]
    public void UseCoreSetup_LocalhostRedis_NoTlsWarning()
    {
        var warnings = RedisWarnings("localhost:6379", prefixKey: "app::");

        Assert.DoesNotContain(warnings, m => m == InsecureRemoteWarning);
    }

    private static List<string> RedisWarnings(string connectionString, string prefixKey)
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Redis:Enabled"] = "true",
            ["CoreSetup:Redis:ConnectionString"] = connectionString,
            ["CoreSetup:Redis:PrefixKey"] = prefixKey
        };

        using var app = IntegrationTestBase.BuildAppWithTestLogging(out var collector, config);

        return collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message)
            .ToList();
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
