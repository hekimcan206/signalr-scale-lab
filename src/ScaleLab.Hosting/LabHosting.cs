using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ScaleLab.Hosting;

public static class LabHosting
{
    /// <summary>Header that carries the lab key for the <c>/api</c> endpoints.</summary>
    public const string KeyHeader = "X-Lab-Key";

    /// <summary>
    /// Structured JSON logs and, when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, OpenTelemetry metrics over OTLP.
    /// </summary>
    /// <remarks>
    /// Per-request logging is off on purpose: at ten thousand connections the negotiate and upgrade requests would turn
    /// the log into the bottleneck being measured. ASP.NET Core's own meters (Kestrel, HTTP connections, hosting) carry
    /// the connection counts and durations instead.
    /// </remarks>
    public static WebApplicationBuilder AddLabObservability(this WebApplicationBuilder builder, string serviceName, string node, params string[] meters)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSerilog(
            (_, lc) => lc
                .ReadFrom.Configuration(builder.Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Yarp", LogEventLevel.Warning)
                .Enrich.WithProperty("service", serviceName)
                .Enrich.WithProperty("node", node)
                .WriteTo.Console(new RenderedCompactJsonFormatter()),
            preserveStaticLogger: true);

        var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(serviceName, serviceInstanceId: node))
                .WithMetrics(m => m
                    .AddAspNetCoreInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel", "Microsoft.AspNetCore.Http.Connections")
                    .AddMeter(meters)
                    .AddOtlpExporter());
        }

        return builder;
    }

    /// <summary>
    /// Requires the configured lab key on a route group. With no key configured the group stays open, which is the
    /// local-demo default and is called out in the threat model.
    /// </summary>
    public static RouteGroupBuilder RequireLabKey(this RouteGroupBuilder group, string? key)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrEmpty(key))
        {
            return group;
        }

        var expected = Encoding.UTF8.GetBytes(key);
        group.AddEndpointFilter(async (context, next) =>
        {
            var supplied = context.HttpContext.Request.Headers[KeyHeader].ToString();
            return KeyMatches(expected, supplied) ? await next(context) : Results.Unauthorized();
        });
        return group;
    }

    /// <summary>Constant-time comparison, so response timing does not reveal how much of the key was right.</summary>
    public static bool KeyMatches(byte[] expected, string? supplied)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>The node name: configured <c>Lab:Node</c>, else the host name (the container name under compose).</summary>
    public static string NodeName(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration["Lab:Node"];
        return string.IsNullOrWhiteSpace(configured) ? Environment.MachineName.ToLowerInvariant() : configured;
    }
}
