using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.Configuration;

namespace BoricuaCoder.API.CoreSetup.Tests.Options;

public class JwtOptionsTests
{
    [Fact]
    public void JwtOptions_DefaultValues_AreCorrect()
    {
        var options = new JwtOptions();

        Assert.Equal(string.Empty, options.Authority);
        Assert.Equal(string.Empty, options.Audience);
        Assert.True(options.RequireHttpsMetadata);
    }

    [Fact]
    public void JwtOptions_CanSetAuthority()
    {
        var options = new JwtOptions { Authority = "https://auth.example.com" };

        Assert.Equal("https://auth.example.com", options.Authority);
    }

    [Fact]
    public void JwtOptions_CanSetAudience()
    {
        var options = new JwtOptions { Audience = "my-api" };

        Assert.Equal("my-api", options.Audience);
    }

    [Fact]
    public void JwtOptions_CanDisableRequireHttpsMetadata()
    {
        var options = new JwtOptions { RequireHttpsMetadata = false };

        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void JwtOptions_TokenValidation_DefaultsToEmptyOptions()
    {
        var options = new JwtOptions();

        Assert.NotNull(options.TokenValidation);
        Assert.Null(options.TokenValidation.ValidateIssuer);
        Assert.Null(options.TokenValidation.ValidateAudience);
        Assert.Null(options.TokenValidation.ValidateLifetime);
        Assert.Null(options.TokenValidation.ClockSkewSeconds);
    }

    [Fact]
    public void JwtOptions_ValidAlgorithms_DefaultsToEmpty()
    {
        Assert.Empty(new JwtOptions().ValidAlgorithms);
    }

    [Fact]
    public void JwtOptions_BindsValidAlgorithmsArray()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:ValidAlgorithms:0"] = "RS256",
                ["Jwt:ValidAlgorithms:1"] = "PS256",
                ["Jwt:ValidAlgorithms:2"] = "ES256"
            })
            .Build();

        var options = config.GetSection("Jwt").Get<JwtOptions>()!;

        Assert.Equal(["RS256", "PS256", "ES256"], options.ValidAlgorithms);
    }
}
