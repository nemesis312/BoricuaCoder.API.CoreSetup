using BoricuaCoder.API.CoreSetup.Options;

namespace BoricuaCoder.API.CoreSetup.Tests.Options;

public class TokenValidationOptionsTests
{
    [Fact]
    public void TokenValidationOptions_AllDefaults_AreNull()
    {
        var options = new TokenValidationOptions();

        Assert.Null(options.ValidateIssuer);
        Assert.Null(options.ValidateAudience);
        Assert.Null(options.ValidateLifetime);
        Assert.Null(options.ClockSkewSeconds);
    }

    [Fact]
    public void TokenValidationOptions_CanDisableIssuerValidation()
    {
        var options = new TokenValidationOptions { ValidateIssuer = false };

        Assert.False(options.ValidateIssuer);
    }

    [Fact]
    public void TokenValidationOptions_CanDisableAudienceValidation()
    {
        var options = new TokenValidationOptions { ValidateAudience = false };

        Assert.False(options.ValidateAudience);
    }

    [Fact]
    public void TokenValidationOptions_CanDisableLifetimeValidation()
    {
        var options = new TokenValidationOptions { ValidateLifetime = false };

        Assert.False(options.ValidateLifetime);
    }

    [Fact]
    public void TokenValidationOptions_CanSetClockSkewToZero()
    {
        var options = new TokenValidationOptions { ClockSkewSeconds = 0 };

        Assert.Equal(0, options.ClockSkewSeconds);
    }

    [Fact]
    public void TokenValidationOptions_CanSetCustomClockSkew()
    {
        var options = new TokenValidationOptions { ClockSkewSeconds = 60 };

        Assert.Equal(60, options.ClockSkewSeconds);
    }
}
