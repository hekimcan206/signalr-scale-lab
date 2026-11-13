using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScaleLab.HubServer;
using ScaleLab.LoadGen;
using ScaleLab.Proxy;
using Testcontainers.Redis;

namespace ScaleLab.IntegrationTests;

/// <summary>
/// The lab topology in one test process: a real Redis in a container, two hub servers and the YARP proxy on real
/// Kestrel sockets (WebSockets need real sockets end to end, so no TestServer).
/// </summary>
public sealed class LabTopology : IAsyncLifetime
{
    public const string Key = "integration-test-key";
    public const string RedisImage = "redis:7.4.11-alpine";

    private readonly RedisContainer _redis = new RedisBuilder(RedisImage).Build();
    private readonly List<WebApplication> _apps = [];

    public Uri HubA { get; private set; } = null!;

    public Uri HubB { get; private set; } = null!;

    public Uri Proxy { get; private set; } = null!;

    internal LabTarget Target => new(HubA, Proxy, [HubA, HubB], Key);

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        var redis = _redis.GetConnectionString();
        HubA = await StartAsync(HubServerApp.Build([], b => Configure(b, ("Lab:Node", "hub-a"), ("Lab:Redis", redis), ("Lab:Key", Key))));
        HubB = await StartAsync(HubServerApp.Build([], b => Configure(b, ("Lab:Node", "hub-b"), ("Lab:Redis", redis), ("Lab:Key", Key))));
        Proxy = await StartAsync(ProxyApp.Build([], b => Configure(
            b, ("Lab:Node", "proxy"), ("Lab:Key", Key), ("Proxy:Destinations", $"{HubA},{HubB}"))));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var app in Enumerable.Reverse(_apps))
        {
            await app.DisposeAsync();
        }

        await _redis.DisposeAsync();
    }

    /// <summary>Starts an extra hub server without Redis, for tests that need a server with no backplane.</summary>
    public async Task<Uri> StartStandaloneHubAsync(string node, string? key = null)
    {
        var settings = new List<(string, string?)> { ("Lab:Node", node) };
        if (key is not null)
        {
            settings.Add(("Lab:Key", key));
        }

        return await StartAsync(HubServerApp.Build([], b => Configure(b, [.. settings])));
    }

    private static void Configure(WebApplicationBuilder builder, params (string Key, string? Value)[] settings)
    {
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
        builder.Configuration.AddInMemoryCollection([new("Serilog:MinimumLevel:Default", "Warning")]);
    }

    private async Task<Uri> StartAsync(WebApplication app)
    {
        await app.StartAsync();
        _apps.Add(app);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new Uri(address);
    }
}

[CollectionDefinition(Name)]
public sealed class LabGroup : ICollectionFixture<LabTopology>
{
    public const string Name = "lab";
}
