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

    private const string SwaggerProductionWarning =
        "CoreSetup: Swagger UI is enabled in the 'Production' environment and exposes the full API surface. It will be disabled by default outside Development in 2.0.0. Set CoreSetup:Swagger:Enabled explicitly.";

    [Fact]
    public void UseCoreSetup_SwaggerInProduction_LogsWarning()
    {
        using var app = IntegrationTestBase.BuildAppWithTestLogging(
            out var collector, environmentName: "Production");

        var warnings = collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message)
            .ToList();

        Assert.Single(warnings, m => m == SwaggerProductionWarning);
    }

    [Fact]
    public void UseCoreSetup_SwaggerInDevelopment_NoProductionWarning()
    {
        var warnings = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message);

        Assert.DoesNotContain(warnings, m => m.Contains("Swagger UI is enabled in"));
    }

    // ── JWT misconfiguration warnings ──

    private const string AudienceAccountWarning =
        "CoreSetup: Jwt:Audience is 'account'. Keycloak adds this audience to tokens of most clients in the realm, so tokens issued for other applications will be accepted. Configure an audience mapper with this API's client id.";

    private const string EmptyAuthorityWarning =
        "CoreSetup: Jwt:Authority is not set; every bearer token will be rejected.";

    private static List<string> JwtWarnings(string key, string value)
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig) { [key] = value };

        using var app = IntegrationTestBase.BuildAppWithTestLogging(out var collector, config);

        return collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message)
            .ToList();
    }

    [Fact]
    public void UseCoreSetup_AudienceAccount_LogsWarning()
    {
        var warnings = JwtWarnings("CoreSetup:Jwt:Audience", "Account");

        Assert.Single(warnings, m => m == AudienceAccountWarning);
    }

    [Fact]
    public void UseCoreSetup_EmptyAuthority_LogsWarning()
    {
        var warnings = JwtWarnings("CoreSetup:Jwt:Authority", "");

        Assert.Single(warnings, m => m == EmptyAuthorityWarning);
    }

    [Fact]
    public void UseCoreSetup_ValidateAudienceFalse_LogsWarning()
    {
        var warnings = JwtWarnings("CoreSetup:Jwt:TokenValidation:ValidateAudience", "false");

        Assert.Single(warnings, m =>
            m == "CoreSetup: Jwt:TokenValidation:ValidateAudience is disabled. Do not use this setting in production.");
    }

    [Fact]
    public void UseCoreSetup_DefaultJwtConfig_NoJwtWarnings()
    {
        var warnings = _collector.GetSnapshot()
            .Where(r => r.Category == CoreSetupExtensions.LogCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message);

        Assert.DoesNotContain(warnings, m => m.Contains("Jwt:"));
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

    private static string PrefixSeparatorWarning(string prefixKey) =>
        $"CoreSetup: Redis PrefixKey '{prefixKey}' does not end with ':'. DeleteAllAsync and cascade deletes may also match keys of other apps whose prefix starts with the same text.";

    [Fact]
    public void UseCoreSetup_PrefixKeyWithoutSeparator_LogsWarning()
    {
        var warnings = RedisWarnings("localhost:6379", prefixKey: "myapp");

        Assert.Single(warnings, m => m == PrefixSeparatorWarning("myapp"));
    }

    [Theory]
    [InlineData("app::")]
    [InlineData("myapp:")]
    [InlineData("")]
    public void UseCoreSetup_PrefixKeyEndingWithSeparatorOrEmpty_NoSeparatorWarning(string prefixKey)
    {
        var warnings = RedisWarnings("localhost:6379", prefixKey);

        Assert.DoesNotContain(warnings, m => m.Contains("does not end with ':'"));
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
