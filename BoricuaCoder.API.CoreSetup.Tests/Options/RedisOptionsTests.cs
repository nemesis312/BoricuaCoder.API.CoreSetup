using BoricuaCoder.API.CoreSetup.Options;

namespace BoricuaCoder.API.CoreSetup.Tests.Options;

public class RedisOptionsTests
{
    [Fact]
    public void DefaultOptions_HasExpectedValues()
    {
        var options = new RedisOptions();

        Assert.False(options.Enabled);
        Assert.Equal(string.Empty, options.PrefixKey);
        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal(300, options.DefaultTTL);
        Assert.Equal(0, options.ShortCircuit);
    }

    [Fact]
    public void Options_CanBeInitializedWithCustomValues()
    {
        var options = new RedisOptions
        {
            Enabled = true,
            PrefixKey = "myapp::",
            ConnectionString = "localhost:6379",
            DefaultTTL = 120,
            ShortCircuit = 3
        };

        Assert.True(options.Enabled);
        Assert.Equal("myapp::", options.PrefixKey);
        Assert.Equal("localhost:6379", options.ConnectionString);
        Assert.Equal(120, options.DefaultTTL);
        Assert.Equal(3, options.ShortCircuit);
    }
}
