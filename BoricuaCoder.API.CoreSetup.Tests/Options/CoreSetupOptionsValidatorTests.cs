using BoricuaCoder.API.CoreSetup.Options;
using Microsoft.Extensions.Options;

namespace BoricuaCoder.API.CoreSetup.Tests.Options;

public class CoreSetupOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(CoreSetupOptions options)
    {
        var validator = new CoreSetupOptionsValidator();
        return validator.Validate(null, options);
    }

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = Validate(new CoreSetupOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ValidJwtAuthority_Succeeds()
    {
        var options = new CoreSetupOptions
        {
            Jwt = new JwtOptions { Authority = "https://auth.example.com" }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_InvalidJwtAuthority_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Jwt = new JwtOptions { Authority = "not-a-valid-uri" }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CoreSetup:Jwt:Authority"));
    }

    [Fact]
    public void Validate_EmptyJwtAuthority_Succeeds()
    {
        var options = new CoreSetupOptions
        {
            Jwt = new JwtOptions { Authority = string.Empty }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ValidOAuthUrls_Succeeds()
    {
        var options = new CoreSetupOptions
        {
            Swagger = new SwaggerOptions
            {
                Enabled = true,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = "https://auth.example.com/auth",
                    TokenUrl = "https://auth.example.com/token"
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_InvalidAuthorizationUrl_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Swagger = new SwaggerOptions
            {
                Enabled = true,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = "not-a-uri",
                    TokenUrl = "https://auth.example.com/token"
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CoreSetup:Swagger:OAuth:AuthorizationUrl"));
    }

    [Fact]
    public void Validate_InvalidTokenUrl_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Swagger = new SwaggerOptions
            {
                Enabled = true,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = "https://auth.example.com/auth",
                    TokenUrl = "bad url here"
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CoreSetup:Swagger:OAuth:TokenUrl"));
    }

    [Fact]
    public void Validate_AuthorizationUrlWithoutTokenUrl_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Swagger = new SwaggerOptions
            {
                Enabled = true,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = "https://auth.example.com/auth",
                    TokenUrl = string.Empty
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("TokenUrl is required"));
    }

    [Fact]
    public void Validate_TokenUrlWithoutAuthorizationUrl_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Swagger = new SwaggerOptions
            {
                Enabled = true,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = string.Empty,
                    TokenUrl = "https://auth.example.com/token"
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("AuthorizationUrl is required"));
    }

    [Fact]
    public void Validate_InvalidOAuthUrls_WhenSwaggerDisabled_Succeeds()
    {
        var options = new CoreSetupOptions
        {
            Swagger = new SwaggerOptions
            {
                Enabled = false,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = "not-a-uri",
                    TokenUrl = "also-not-a-uri"
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    // ── Redis validation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_RedisDisabled_NoValidationApplied()
    {
        var options = new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = false, ConnectionString = string.Empty }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RedisEnabled_WithConnectionString_Succeeds()
    {
        var options = new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = true, ConnectionString = "localhost:6379" }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RedisEnabled_EmptyConnectionString_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = true, ConnectionString = string.Empty }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CoreSetup:Redis:ConnectionString"));
    }

    [Fact]
    public void Validate_RedisEnabled_ZeroDefaultTTL_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = true, ConnectionString = "localhost:6379", DefaultTTL = 0 }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CoreSetup:Redis:DefaultTTL"));
    }

    [Fact]
    public void Validate_RedisEnabled_NegativeShortCircuit_FailsWithMessage()
    {
        var options = new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = true, ConnectionString = "localhost:6379", ShortCircuit = -1 }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CoreSetup:Redis:ShortCircuit"));
    }

    [Fact]
    public void Validate_RedisEnabled_ZeroShortCircuit_Succeeds()
    {
        var options = new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = true, ConnectionString = "localhost:6379", ShortCircuit = 0 }
        };

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleErrors_ReturnsAllFailures()
    {
        var options = new CoreSetupOptions
        {
            Jwt = new JwtOptions { Authority = "bad-authority" },
            Swagger = new SwaggerOptions
            {
                Enabled = true,
                OAuth = new SwaggerOAuthOptions
                {
                    AuthorizationUrl = "bad-auth-url",
                    TokenUrl = "bad-token-url"
                }
            }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.True(result.Failures!.Count() >= 3);
    }
}
