using System.Diagnostics;
using ScaleLab.Core.Measurement;
using ScaleLab.Core.Scenarios;

namespace ScaleLab.LoadGen;

/// <summary>Where the lab's servers are.</summary>
/// <param name="Direct">First hub server; broadcasts are always sent here, and the single-node scenario connects here.</param>
/// <param name="Proxy">The YARP proxy.</param>
/// <param name="Servers">Every hub server, for stats.</param>
internal sealed record LabTarget(Uri Direct, Uri Proxy, IReadOnlyList<Uri> Servers, string? Key);

internal sealed record BenchOptions(
    Scenario Scenario,
    IReadOnlyList<int> Stages,
    int Messages,
    double MessagesPerSecond,
    int PayloadBytes,
    int Concurrency,
    TimeSpan Settle,
    TimeSpan Drain,
    int Repetition,
    string Host);

/// <summary>
/// One benchmark run: warm up, take an idle baseline, then ramp the connection count stage by stage and at each plateau
/// measure memory (after a forced full GC everywhere) and broadcast fan-out.
/// </summary>
internal static class BenchRunner
{
    public static async Task<RunResult> RunAsync(LabTarget target, BenchOptions options, TextWriter log, CancellationToken ct)
    {
        using var api = LabApi.Create(target.Key);
        await api.WaitHealthyAsync([.. target.Servers, target.Proxy], TimeSpan.FromSeconds(90), ct);
        var started = DateTimeOffset.UtcNow;
        var url = options.Scenario.ClientUrl(target.Direct, target.Proxy);

        // Warm-up: JIT, connection pools and SignalR's first-use allocations would otherwise be charged to the first
        // stage's connections.
        await using (var warm = new ClientSwarm(url, options.Scenario.SkipNegotiation))
        {
            await warm.OpenAsync(60, 30, ct);
            await FanoutProbe.MeasureAsync(warm, api, target.Direct, options.Scenario.HubName, 3, 10, options.PayloadBytes, TimeSpan.FromSeconds(2), ct);
        }

        await Task.Delay(options.Settle, ct);
        var baseline = await SnapshotAsync(api, target, ct);
        log.WriteLine($"[{options.Scenario.Name} #{options.Repetition}] baseline: servers {Mib(baseline.ServerWorkingSetBytes)} MiB RSS, proxy {Mib(baseline.ProxyWorkingSetBytes)} MiB, client {Mib(baseline.ClientWorkingSetBytes)} MiB");

        var stages = new List<StageResult>();
        await using var swarm = new ClientSwarm(url, options.Scenario.SkipNegotiation);
        foreach (var stageTarget in options.Stages)
        {
            var toOpen = stageTarget - swarm.Attempted;
            var ramp = toOpen > 0 ? await swarm.OpenAsync(toOpen, options.Concurrency, ct) : TimeSpan.Zero;
            await Task.Delay(options.Settle, ct);

            var memory = await SnapshotAsync(api, target, ct);
            var perServer = memory.Processes
                .Where(p => p.Role == "hub")
                .ToDictionary(p => p.Node, p => p.Connections.GetValueOrDefault(options.Scenario.HubName));
            var cpuBefore = await CpuSecondsAsync(api, target, ct);
            var fanout = await FanoutProbe.MeasureAsync(
                swarm, api, target.Direct, options.Scenario.HubName, options.Messages, options.MessagesPerSecond, options.PayloadBytes, options.Drain, ct);
            var cpuAfter = await CpuSecondsAsync(api, target, ct);
            var window = (cpuAfter.At - cpuBefore.At).TotalSeconds;
            var cores = cpuAfter.Seconds.ToDictionary(kv => kv.Key, kv => Math.Round((kv.Value - cpuBefore.Seconds.GetValueOrDefault(kv.Key)) / window, 3));

            var stage = new StageResult(
                stageTarget,
                swarm.Connected,
                swarm.Failed,
                swarm.Dropped,
                swarm.FailureReasons,
                Math.Round(ramp.TotalSeconds, 3),
                perServer,
                memory,
                fanout,
                cores);
            stages.Add(stage);
            log.WriteLine(
                $"[{options.Scenario.Name} #{options.Repetition}] {stageTarget,6} attempted: {stage.Connected} connected, {stage.Failed} failed, {stage.Dropped} dropped, " +
                $"ramp {ramp.TotalSeconds:0.0}s | servers {Mib(memory.ServerWorkingSetBytes)} MiB, proxy {Mib(memory.ProxyWorkingSetBytes)} MiB, client {Mib(memory.ClientWorkingSetBytes)} MiB | " +
                $"delivery {fanout.DeliveryRatio * 100:0.0}%, p50 {fanout.LatencyMs.P50:0.0} ms, p99 {fanout.LatencyMs.P99:0.0} ms, last-client p50 {fanout.CompletionMs.P50:0.0} ms");
        }

        var settings = new RunSettings(
            target.Servers.Count, options.Messages, options.MessagesPerSecond, options.PayloadBytes, options.Concurrency, options.Host);
        return new RunResult(options.Scenario.Name, options.Repetition, started, settings, baseline, stages);
    }

    internal static async Task<MemorySnapshot> SnapshotAsync(LabApi api, LabTarget target, CancellationToken ct)
    {
        var processes = new List<ProcessStats>();
        foreach (var server in target.Servers)
        {
            processes.Add(await api.StatsAsync(server, forceGc: true, ct));
        }

        var proxy = await api.StatsAsync(target.Proxy, forceGc: true, ct);
        processes.Add(proxy);
        var (clientWs, clientHeap) = ClientSwarm.SampleSelf();
        var hubs = processes.Where(p => p.Role == "hub").ToList();
        return new MemorySnapshot(
            hubs.Sum(p => p.WorkingSetBytes),
            hubs.Sum(p => p.GcHeapBytes),
            proxy.WorkingSetBytes,
            proxy.GcHeapBytes,
            clientWs,
            clientHeap,
            processes);
    }

    private static async Task<(DateTime At, Dictionary<string, double> Seconds)> CpuSecondsAsync(LabApi api, LabTarget target, CancellationToken ct)
    {
        var seconds = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var server in target.Servers.Append(target.Proxy))
        {
            var stats = await api.StatsAsync(server, forceGc: false, ct);
            seconds[stats.Node] = stats.CpuSeconds;
        }

        using var self = System.Diagnostics.Process.GetCurrentProcess();
        seconds["loadgen"] = self.TotalProcessorTime.TotalSeconds;
        return (DateTime.UtcNow, seconds);
    }

    private static string Mib(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Sends numbered broadcasts at a fixed rate and waits for them to arrive.</summary>
internal static class FanoutProbe
{
    public static async Task<FanoutSummary> MeasureAsync(
        ClientSwarm swarm,
        LabApi api,
        Uri broadcastServer,
        string hub,
        int messages,
        double perSecond,
        int payloadBytes,
        TimeSpan drain,
        CancellationToken ct)
    {
        var ledger = new FanoutLedger(messages, Math.Max(swarm.Connected, 1));
        swarm.UseLedger(ledger);
        try
        {
            var period = TimeSpan.FromSeconds(1 / perSecond);
            var start = Stopwatch.GetTimestamp();
            for (var seq = 0; seq < messages; seq++)
            {
                var due = period * seq;
                var wait = due - Stopwatch.GetElapsedTime(start);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                ledger.MarkSent(seq, Stopwatch.GetTimestamp(), swarm.Connected);
                await api.BroadcastAsync(broadcastServer, hub, seq, payloadBytes, ct);
            }

            // Wait for the stragglers. Without a backplane most clients never receive, so this runs to the timeout.
            var drainStart = Stopwatch.GetTimestamp();
            while (ledger.TotalReceived < ledger.TotalExpected && Stopwatch.GetElapsedTime(drainStart) < drain)
            {
                await Task.Delay(50, ct);
            }

            return ledger.Summarize(Stopwatch.Frequency);
        }
        finally
        {
            swarm.UseLedger(null);
        }
    }
}
