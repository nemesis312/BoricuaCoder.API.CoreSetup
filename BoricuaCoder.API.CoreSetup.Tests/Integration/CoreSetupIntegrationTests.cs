using System.Net;
using BoricuaCoder.API.CoreSetup.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration;

public class CoreSetupIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _app = IntegrationTestBase.BuildApp();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task UseCoreSetup_SwaggerEnabled_SwaggerJsonReturns200()
    {
        var response = await _client!.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UseCoreSetup_SwaggerEnabled_SwaggerJsonContainsApiTitle()
    {
        var response = await _client!.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Test API", body);
    }

    [Fact]
    public async Task UseCoreSetup_AuthenticationMiddleware_IsRegistered()
    {
        var serviceProvider = _app!.Services;
        var schemeProvider = serviceProvider.GetService<IAuthenticationSchemeProvider>();

        Assert.NotNull(schemeProvider);
        var schemes = await schemeProvider!.GetAllSchemesAsync();
        Assert.Contains(schemes, s => s.Name == "Bearer");
    }

    [Fact]
    public async Task UseCoreSetup_HealthEndpoint_Returns200()
    {
        var response = await _client!.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UseCoreSetup_ReturnsWebApplicationForChaining()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(IntegrationTestBase.DefaultConfig);
        builder.Services.AddCoreSetup(builder.Configuration);

        var app = builder.Build();
        var result = app.UseCoreSetup();

        Assert.Same(app, result);

        await app.DisposeAsync();
    }
}

public class CoreSetupSwaggerDisabledIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var config = new Dictionary<string, string?>(IntegrationTestBase.DefaultConfig)
        {
            ["CoreSetup:Swagger:Enabled"] = "false"
        };

        _app = IntegrationTestBase.BuildApp(config);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task UseCoreSetup_SwaggerDisabled_SwaggerJsonReturns404()
    {
        var response = await _client!.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UseCoreSetup_SwaggerDisabled_SwaggerUiReturns404()
    {
        var response = await _client!.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
