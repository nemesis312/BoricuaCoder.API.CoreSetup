using BoricuaCoder.API.CoreSetup.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration;

internal static class IntegrationTestBase
{
    internal static readonly Dictionary<string, string?> DefaultConfig = new()
    {
        ["CoreSetup:Jwt:Authority"] = "https://auth.example.com",
        ["CoreSetup:Jwt:Audience"] = "test-api",
        ["CoreSetup:Jwt:RequireHttpsMetadata"] = "false",
        ["CoreSetup:Swagger:Enabled"] = "true",
        ["CoreSetup:Swagger:Title"] = "Test API",
        ["CoreSetup:Swagger:Version"] = "v1",
        ["CoreSetup:Swagger:RoutePrefix"] = "swagger",
        ["CoreSetup:Swagger:OAuth:AuthorizationUrl"] = "https://auth.example.com/realms/test/protocol/openid-connect/auth",
        ["CoreSetup:Swagger:OAuth:TokenUrl"] = "https://auth.example.com/realms/test/protocol/openid-connect/token",
        ["CoreSetup:Swagger:OAuth:ClientId"] = "test-client"
    };

    internal static WebApplication BuildApp(Dictionary<string, string?>? config = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(config ?? DefaultConfig);
        builder.Services.AddCoreSetup(builder.Configuration);

        var app = builder.Build();
        app.UseCoreSetup();
        app.MapGet("/health", () => Results.Ok("healthy"));

        return app;
    }

    internal static WebApplication BuildAppWithTestLogging(
        out TestLogCollector collector,
        Dictionary<string, string?>? config = null)
    {
        var testCollector = new TestLogCollector();
        collector = testCollector;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(config ?? DefaultConfig);
        builder.Services.AddCoreSetup(builder.Configuration);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddTestLogging(testCollector);

        var app = builder.Build();
        app.UseCoreSetup();
        app.MapGet("/health", () => Results.Ok("healthy"));

        return app;
    }
}
