using ScaleLab.Core.Measurement;
using ScaleLab.Core.Scenarios;

namespace ScaleLab.Core.Reporting;

/// <summary>Median over repetitions of one scenario at one connection target.</summary>
/// <param name="Runs">Repetitions that reached this stage.</param>
/// <param name="ConnectRatio">Connected / attempted.</param>
/// <param name="ServerRssPerConnectionKiB">Growth of the hub servers' summed RSS over the idle baseline, per connection.</param>
/// <param name="ServerHeapPerConnectionKiB">Growth of the summed managed heap after a full GC, per connection.</param>
/// <param name="ProxyRssPerConnectionKiB">Growth of the proxy's RSS per proxied connection (null without a proxy).</param>
/// <param name="ClientRssPerConnectionKiB">Growth of the load generator's RSS per client connection.</param>
public sealed record StageAggregate(
    string Scenario,
    int Target,
    int Runs,
    double Connected,
    double ConnectRatio,
    double DeliveryRatio,
    double LatencyP50Ms,
    double LatencyP99Ms,
    double CompletionP50Ms,
    double ServerRssMiB,
    double ServerRssPerConnectionKiB,
    double ServerHeapPerConnectionKiB,
    double? ProxyRssPerConnectionKiB,
    double ClientRssPerConnectionKiB);

public static class Aggregation
{
    private const double KiB = 1024.0;
    private const double MiB = 1024.0 * 1024.0;

    /// <summary>
    /// Groups runs by scenario and connection target and takes the median of every metric across repetitions.
    /// Latency percentiles are per-run percentiles first, then the median of those, so one slow run cannot hide in a
    /// pooled sample and one fast run cannot hide a slow one.
    /// </summary>
    public static IReadOnlyList<StageAggregate> Aggregate(IEnumerable<RunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var order = ScenarioCatalog.All.Select((s, i) => (s.Name, i)).ToDictionary(t => t.Name, t => t.i);

        var rows = runs
            .SelectMany(run => run.Stages.Select(stage => (run, stage)))
            .GroupBy(x => (x.run.Scenario, x.stage.Target))
            .Select(g => Row(g.Key.Scenario, g.Key.Target, g.ToList()))
            .OrderBy(r => order.GetValueOrDefault(r.Scenario, int.MaxValue))
            .ThenBy(r => r.Target)
            .ToList();
        return rows;
    }

    private static StageAggregate Row(string scenario, int target, List<(RunResult Run, StageResult Stage)> items)
    {
        var direct = ScenarioCatalog.All.FirstOrDefault(s => s.Name == scenario)?.Entry == Entry.Direct;

        double PerConnection(Func<MemorySnapshot, long> pick, (RunResult Run, StageResult Stage) x) =>
            x.Stage.Connected == 0 ? 0 : (pick(x.Stage.Memory) - pick(x.Run.Baseline)) / KiB / x.Stage.Connected;

        double Med(Func<(RunResult Run, StageResult Stage), double> f) => Statistics.Median(items.Select(f));

        return new StageAggregate(
            scenario,
            target,
            items.Count,
            Med(x => x.Stage.Connected),
            Med(x => target == 0 ? 0 : (double)(x.Stage.Target - x.Stage.Failed) / x.Stage.Target),
            Med(x => x.Stage.Fanout.DeliveryRatio),
            Med(x => x.Stage.Fanout.LatencyMs.P50),
            Med(x => x.Stage.Fanout.LatencyMs.P99),
            Med(x => x.Stage.Fanout.CompletionMs.P50),
            Med(x => x.Stage.Memory.ServerWorkingSetBytes / MiB),
            Med(x => PerConnection(m => m.ServerWorkingSetBytes, x)),
            Med(x => PerConnection(m => m.ServerHeapBytes, x)),
            direct ? null : Med(x => PerConnection(m => m.ProxyWorkingSetBytes, x)),
            Med(x => PerConnection(m => m.ClientWorkingSetBytes, x)));
    }
}
