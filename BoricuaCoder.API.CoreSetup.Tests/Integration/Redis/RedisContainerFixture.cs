using Testcontainers.Redis;

namespace BoricuaCoder.API.CoreSetup.Tests.Integration.Redis;

public sealed class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisContainerFixture>;
