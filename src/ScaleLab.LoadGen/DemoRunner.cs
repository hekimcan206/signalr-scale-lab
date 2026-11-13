using System.Globalization;
using System.Text.Json;
using ScaleLab.Core.Measurement;
using ScaleLab.Core.Scenarios;

namespace ScaleLab.LoadGen;

/// <summary>One scenario's outcome in the demo, and whether it matched the topology's expected behaviour.</summary>
internal sealed record DemoOutcome(
    string Scenario,
    int Attempted,
    int Connected,
    double ConnectRatio,
    double DeliveryRatio,
    double LatencyP50Ms,
    double LatencyP99Ms,
    IReadOnlyDictionary<string, int> ConnectionsPerServer,
    bool AsExpected,
    string Expectation);

/// <summary>
/// The <c>docker compose up</c> demo: every scenario at a few hundred connections, one after the other, with the
/// outcome checked against what the topology should do. Exit code 0 only if all five behave as expected.
/// </summary>
internal static class DemoRunner
{
    public static async Task<int> RunAsync(LabTarget target, int connections, TextWriter output, CancellationToken ct)
    {
        using var api = LabApi.Create(target.Key);
        await api.WaitHealthyAsync([.. target.Servers, target.Proxy], TimeSpan.FromSeconds(90), ct);
        var servers = target.Servers.Count;
        var outcomes = new List<DemoOutcome>();

        output.WriteLine($"SignalR scale-out demo: {servers} hub servers behind a YARP proxy, Redis backplane, {connections} clients per scenario");
        output.WriteLine();
        output.WriteLine($"{"scenario",-14} {"connected",12} {"delivery",9} {"p50 ms",8} {"p99 ms",8}  per server              verdict");

        foreach (var scenario in ScenarioCatalog.All)
        {
            var outcome = await RunScenarioAsync(target, api, scenario, connections, servers, ct);
            outcomes.Add(outcome);
            var spread = string.Join(" ", outcome.ConnectionsPerServer.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{scenario.Name,-14} {outcome.Connected,5}/{outcome.Attempted,-6} {(outcome.DeliveryRatio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",9} {outcome.LatencyP50Ms,8:0.0} {outcome.LatencyP99Ms,8:0.0}  {spread,-23} {(outcome.AsExpected ? "ok" : "UNEXPECTED")}: {outcome.Expectation}"));
        }

        var all = outcomes.TrueForAll(o => o.AsExpected);
        output.WriteLine();
        output.WriteLine(JsonSerializer.Serialize(new { demo = "signalr-scale-lab", servers, connections, allAsExpected = all, outcomes }, LabApi.Json));
        return all ? 0 : 1;
    }

    internal static async Task<DemoOutcome> RunScenarioAsync(
        LabTarget target, LabApi api, Scenario scenario, int connections, int servers, CancellationToken ct)
    {
        await using var swarm = new ClientSwarm(scenario.ClientUrl(target.Direct, target.Proxy), scenario.SkipNegotiation);
        await swarm.OpenAsync(connections, 50, ct);
        await Task.Delay(500, ct);

        var perServer = new Dictionary<string, int>();
        foreach (var server in target.Servers)
        {
            var stats = await api.StatsAsync(server, forceGc: false, ct);
            perServer[stats.Node] = stats.Connections.GetValueOrDefault(scenario.HubName);
        }

        var fanout = swarm.Connected == 0
            ? new FanoutSummary(0, 0, 0, 0, 0, LatencySummary.Empty, LatencySummary.Empty, 0)
            : await FanoutProbe.MeasureAsync(swarm, api, target.Direct, scenario.HubName, 5, 5, 32, TimeSpan.FromSeconds(3), ct);

        var connectRatio = (double)swarm.Connected / connections;
        var (ok, expectation) = Judge(scenario, servers, connectRatio, fanout.DeliveryRatio);
        return new DemoOutcome(
            scenario.Name,
            connections,
            swarm.Connected,
            Math.Round(connectRatio, 4),
            Math.Round(fanout.DeliveryRatio, 4),
            Math.Round(fanout.LatencyMs.P50, 2),
            Math.Round(fanout.LatencyMs.P99, 2),
            perServer,
            ok,
            expectation);
    }

    /// <summary>
    /// Compares a measured outcome with the topology's expected behaviour. Ratios that depend on round-robin placement
    /// get a tolerance band; ratios that must be exact (a backplane delivers to everyone) do not.
    /// </summary>
    internal static (bool Ok, string Expectation) Judge(Scenario scenario, int servers, double connectRatio, double deliveryRatio)
    {
        var expectedConnect = scenario.ExpectedConnectRatio(servers);
        var expectedDelivery = scenario.ExpectedDeliveryRatio(servers);
        var connectOk = expectedConnect >= 1.0 ? connectRatio >= 1.0 : connectRatio < 1.0 && Math.Abs(connectRatio - expectedConnect) <= 0.25;
        var deliveryOk = expectedDelivery >= 1.0 ? deliveryRatio >= 1.0 : deliveryRatio < 1.0 && Math.Abs(deliveryRatio - expectedDelivery) <= 0.25;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"expected connect {(expectedConnect >= 1 ? "100%" : $"~{expectedConnect * 100:0}%")}, delivery {(expectedDelivery >= 1 ? "100%" : $"~{expectedDelivery * 100:0}%")}");
        return (connectOk && deliveryOk, text);
    }
}
