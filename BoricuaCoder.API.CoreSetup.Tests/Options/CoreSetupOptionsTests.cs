using BoricuaCoder.API.CoreSetup.Options;

namespace BoricuaCoder.API.CoreSetup.Tests.Options;

public class CoreSetupOptionsTests
{
    [Fact]
    public void CoreSetupOptions_DefaultValues_AreCorrect()
    {
        var options = new CoreSetupOptions();

        Assert.NotNull(options.Jwt);
        Assert.NotNull(options.Swagger);
    }

    [Fact]
    public void CoreSetupOptions_CanSetJwtOptions()
    {
        var jwtOptions = new JwtOptions
        {
            Authority = "https://auth.example.com",
            Audience = "my-api"
        };

        var options = new CoreSetupOptions { Jwt = jwtOptions };

        Assert.Equal("https://auth.example.com", options.Jwt.Authority);
        Assert.Equal("my-api", options.Jwt.Audience);
    }

    [Fact]
    public void CoreSetupOptions_CanSetSwaggerOptions()
    {
        var swaggerOptions = new SwaggerOptions
        {
            Title = "My API",
            Version = "v2"
        };

        var options = new CoreSetupOptions { Swagger = swaggerOptions };

        Assert.Equal("My API", options.Swagger.Title);
        Assert.Equal("v2", options.Swagger.Version);
    }
}
