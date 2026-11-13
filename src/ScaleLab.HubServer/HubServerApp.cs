using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ScaleLab.Core.Measurement;
using ScaleLab.Hosting;
using StackExchange.Redis;

namespace ScaleLab.HubServer;

/// <summary>Builds the hub server. Shared by <c>Program</c> and the integration tests, which host it in-process.</summary>
public static class HubServerApp
{
    /// <summary>Client method every broadcast invokes: <c>tick(long sequence, string payload)</c>.</summary>
    public const string TickMethod = "tick";

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var node = LabHosting.NodeName(builder.Configuration);
        builder.AddLabObservability("scalelab-hub", node, ConnectionTracker.MeterName);
        builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

        var options = builder.Configuration.GetSection(HubServerOptions.Section).Get<HubServerOptions>() ?? new HubServerOptions();
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton<ConnectionTracker>();
        builder.Services.AddHealthChecks();

        var signalR = builder.Services.AddSignalR();
        var backplane = !string.IsNullOrWhiteSpace(options.Redis);
        if (backplane)
        {
            signalR.AddStackExchangeRedis(options.Redis!, redis =>
            {
                redis.Configuration.ChannelPrefix = RedisChannel.Literal(options.ChannelPrefix);

                // Start even if Redis is not up yet and keep retrying; the health of the backplane is visible in the
                // delivery ratio, which is the thing the lab measures.
                redis.Configuration.AbortOnConnectFail = false;
            });
        }

        // AddStackExchangeRedis registers the Redis lifetime manager for every hub (an open generic). A closed
        // registration wins over an open one in Microsoft.Extensions.DependencyInjection, so LocalHub keeps the
        // in-memory manager on the same server, and one deployment can show both behaviours side by side.
        builder.Services.AddSingleton<HubLifetimeManager<LocalHub>, DefaultHubLifetimeManager<LocalHub>>();

        var app = builder.Build();

        app.MapHub<FanoutHub>("/hubs/fanout");
        app.MapHub<LocalHub>("/hubs/local");
        app.MapHealthChecks("/healthz");

        var api = app.MapGroup("/api").RequireLabKey(options.Key);

        api.MapPost("/broadcast/{hub}", async (
            string hub,
            long seq,
            int? bytes,
            IHubContext<FanoutHub> fanout,
            IHubContext<LocalHub> local,
            CancellationToken ct) =>
        {
            var size = bytes ?? 32;
            if (size < 0 || size > options.MaxPayloadBytes)
            {
                return Results.Problem(
                    $"bytes must be between 0 and {options.MaxPayloadBytes}.", statusCode: StatusCodes.Status400BadRequest);
            }

            var clients = hub switch
            {
                FanoutHub.Name => fanout.Clients,
                LocalHub.Name => local.Clients,
                _ => null,
            };
            if (clients is null)
            {
                return Results.Problem($"Unknown hub '{hub}'. Use 'fanout' or 'local'.", statusCode: StatusCodes.Status404NotFound);
            }

            await clients.All.SendAsync(TickMethod, seq, new string('x', size), ct);
            return Results.Accepted(value: new { hub, seq, node });
        });

        api.MapGet("/stats", (bool? gc, ConnectionTracker tracker) =>
            ProcessSampler.Sample(node, "hub", tracker.Snapshot(), backplane, gc == true));

        return app;
    }
}
