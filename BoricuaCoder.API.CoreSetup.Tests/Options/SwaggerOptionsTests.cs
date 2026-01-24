using BoricuaCoder.API.CoreSetup.Options;

namespace BoricuaCoder.API.CoreSetup.Tests.Options;

public class SwaggerOptionsTests
{
    [Fact]
    public void SwaggerOptions_DefaultValues_AreCorrect()
    {
        var options = new SwaggerOptions();

        Assert.True(options.Enabled);
        Assert.Equal("API", options.Title);
        Assert.Equal("v1", options.Version);
        Assert.Equal("swagger", options.RoutePrefix);
        Assert.NotNull(options.OAuth);
    }

    [Fact]
    public void SwaggerOptions_CanBeDisabled()
    {
        var options = new SwaggerOptions { Enabled = false };

        Assert.False(options.Enabled);
    }

    [Fact]
    public void SwaggerOptions_CanSetCustomTitle()
    {
        var options = new SwaggerOptions { Title = "My Custom API" };

        Assert.Equal("My Custom API", options.Title);
    }

    [Fact]
    public void SwaggerOptions_CanSetCustomVersion()
    {
        var options = new SwaggerOptions { Version = "v2" };

        Assert.Equal("v2", options.Version);
    }

    [Fact]
    public void SwaggerOptions_CanSetCustomRoutePrefix()
    {
        var options = new SwaggerOptions { RoutePrefix = "api-docs" };

        Assert.Equal("api-docs", options.RoutePrefix);
    }
}

public class SwaggerOAuthOptionsTests
{
    [Fact]
    public void SwaggerOAuthOptions_DefaultValues_AreCorrect()
    {
        var options = new SwaggerOAuthOptions();

        Assert.Equal(string.Empty, options.AuthorizationUrl);
        Assert.Equal(string.Empty, options.TokenUrl);
        Assert.Equal(string.Empty, options.ClientId);
        Assert.NotNull(options.Scopes);
        Assert.Single(options.Scopes);
        Assert.True(options.Scopes.ContainsKey("openid"));
    }

    [Fact]
    public void SwaggerOAuthOptions_CanSetAuthorizationUrl()
    {
        var options = new SwaggerOAuthOptions
        {
            AuthorizationUrl = "https://auth.example.com/authorize"
        };

        Assert.Equal("https://auth.example.com/authorize", options.AuthorizationUrl);
    }

    [Fact]
    public void SwaggerOAuthOptions_CanSetTokenUrl()
    {
        var options = new SwaggerOAuthOptions
        {
            TokenUrl = "https://auth.example.com/token"
        };

        Assert.Equal("https://auth.example.com/token", options.TokenUrl);
    }

    [Fact]
    public void SwaggerOAuthOptions_CanSetClientId()
    {
        var options = new SwaggerOAuthOptions { ClientId = "my-client" };

        Assert.Equal("my-client", options.ClientId);
    }

    [Fact]
    public void SwaggerOAuthOptions_CanSetCustomScopes()
    {
        var customScopes = new Dictionary<string, string>
        {
            { "openid", "OpenID Connect" },
            { "profile", "User profile" },
            { "email", "Email address" }
        };

        var options = new SwaggerOAuthOptions { Scopes = customScopes };

        Assert.Equal(3, options.Scopes.Count);
        Assert.True(options.Scopes.ContainsKey("profile"));
        Assert.True(options.Scopes.ContainsKey("email"));
    }
}
