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

    // ── Redis connection string parsing ───────────────────────────────────────

    private static ValidateOptionsResult ValidateRedis(string connectionString) =>
        Validate(new CoreSetupOptions
        {
            Redis = new RedisOptions { Enabled = true, ConnectionString = connectionString }
        });

    [Fact]
    public void Validate_RedisEnabled_BogusKeyword_FailsWithoutEchoingValue()
    {
        var result = ValidateRedis("host:6379,password=Hunter2xyz,bogus=1");

        Assert.True(result.Failed);
        Assert.Contains(
            "CoreSetup:Redis:ConnectionString is not a valid StackExchange.Redis configuration string.",
            result.Failures!);
        Assert.DoesNotContain(result.Failures!, f => f.Contains("Hunter2xyz") || f.Contains("bogus"));
    }

    [Theory]
    [InlineData("password=Hunter2xyz")]
    [InlineData("ssl=true,abortConnect=false")]
    public void Validate_RedisEnabled_NoEndpoints_Fails(string connectionString)
    {
        var result = ValidateRedis(connectionString);

        Assert.True(result.Failed);
        Assert.Contains(
            "CoreSetup:Redis:ConnectionString must contain at least one host:port endpoint.",
            result.Failures!);
        Assert.DoesNotContain(result.Failures!, f => f.Contains("Hunter2xyz"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t  ")]
    public void Validate_RedisEnabled_WhitespaceConnectionString_FailsAsRequired(string connectionString)
    {
        var result = ValidateRedis(connectionString);

        Assert.True(result.Failed);
        Assert.Contains("CoreSetup:Redis:ConnectionString is required when Redis is enabled.", result.Failures!);
    }

    [Fact]
    public void Validate_RedisEnabled_ValidStringWithPassword_Succeeds()
    {
        var result = ValidateRedis("redis.example.com:6380,password=Hunter2xyz,ssl=true,abortConnect=false");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RedisEnabled_InvalidStringWithPassword_ErrorsNeverContainPassword()
    {
        var result = Validate(new CoreSetupOptions
        {
            Redis = new RedisOptions
            {
                Enabled = true,
                ConnectionString = "redis.example.com:6380,password=Hunter2xyz,nope=1",
                DefaultTTL = 0
            }
        });

        Assert.True(result.Failed);
        Assert.DoesNotContain(result.Failures!, f => f.Contains("Hunter2xyz"));
    }

    [Theory]
    [InlineData("redis://user:Hunter2xyz@host:6379")]
    [InlineData("rediss://user:Hunter2xyz@host:6380")]
    [InlineData("REDIS://user:Hunter2xyz@host:6379")]
    [InlineData("  Rediss://user:Hunter2xyz@host:6380")]
    public void Validate_RedisEnabled_UriFormat_FailsWithoutEchoingValue(string connectionString)
    {
        var result = ValidateRedis(connectionString);

        Assert.True(result.Failed);
        Assert.Contains(
            "CoreSetup:Redis:ConnectionString uses URI format (redis://), which StackExchange.Redis does not support. Use \"host:port,password=...,ssl=true\".",
            result.Failures!);
        Assert.DoesNotContain(result.Failures!, f => f.Contains("Hunter2xyz") || f.Contains("user:"));
    }
}
