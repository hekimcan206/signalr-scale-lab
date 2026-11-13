using Microsoft.AspNetCore.Http.Features;
using ScaleLab.Core.Measurement;
using ScaleLab.Hosting;

namespace ScaleLab.Proxy;

/// <summary>Builds the proxy. Shared by <c>Program</c> and the integration tests.</summary>
public static class ProxyApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var node = LabHosting.NodeName(builder.Configuration);
        builder.AddLabObservability("scalelab-proxy", node);
        builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

        var destinations = (builder.Configuration["Proxy:Destinations"] ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        builder.Services.AddReverseProxy()
            .LoadFromMemory(ProxyTopology.Routes(), ProxyTopology.Clusters(destinations));
        builder.Services.AddHealthChecks();

        var app = builder.Build();
        var upgraded = new UpgradedConnectionCounter();

        // Counts proxied WebSocket (upgrade) requests while they are open, for per-connection proxy memory.
        app.Use(async (context, next) =>
        {
            if (context.Features.Get<IHttpUpgradeFeature>()?.IsUpgradableRequest != true)
            {
                await next(context);
                return;
            }

            upgraded.Opened();
            try
            {
                await next(context);
            }
            finally
            {
                upgraded.Closed();
            }
        });

        app.MapHealthChecks("/healthz");
        app.MapGroup("/api").RequireLabKey(builder.Configuration["Lab:Key"])
            .MapGet("/stats", (bool? gc) => ProcessSampler.Sample(
                node,
                "proxy",
                new Dictionary<string, int> { ["websockets"] = upgraded.Current },
                backplane: false,
                gc == true));
        app.MapReverseProxy();
        return app;
    }

    private sealed class UpgradedConnectionCounter
    {
        private int _current;

        public int Current => Volatile.Read(ref _current);

        public void Opened() => Interlocked.Increment(ref _current);

        public void Closed() => Interlocked.Decrement(ref _current);
    }
}
