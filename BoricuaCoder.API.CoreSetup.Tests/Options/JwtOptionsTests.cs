using BoricuaCoder.API.CoreSetup.Options;

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
}
