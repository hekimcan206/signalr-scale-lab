using System.Globalization;
using System.Text;
using ScaleLab.Core.Scenarios;

namespace ScaleLab.Core.Reporting;

/// <summary>Turns aggregated measurements into the markdown table and the SVG charts committed to the repository.</summary>
public static class Report
{
    // Fixed colour slot per scenario: the colour follows the scenario across every chart.
    private static readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal)
    {
        [ScenarioCatalog.SingleNode.Name] = 1,
        [ScenarioCatalog.StickyOnly.Name] = 2,
        [ScenarioCatalog.StickyRedis.Name] = 3,
        [ScenarioCatalog.RoundRobinSkip.Name] = 4,
        [ScenarioCatalog.RoundRobinNegotiate.Name] = 5,
    };

    public static string Markdown(IReadOnlyList<StageAggregate> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        sb.Append("| Scenario | Attempted | Connected | Delivered | Latency p50 | Latency p99 | Last client p50 | Hub RSS total | Hub RSS / conn | Hub heap / conn | Proxy RSS / conn | Client RSS / conn | Runs |\n");
        sb.Append("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|\n");
        foreach (var r in rows)
        {
            sb.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"| {r.Scenario} | {r.Target:#,0} | {r.Connected:#,0} | {r.DeliveryRatio * 100:0.0}% | {r.LatencyP50Ms:0.0} ms | {r.LatencyP99Ms:0.0} ms | {Completion(r)} | {r.ServerRssMiB:0} MiB | {r.ServerRssPerConnectionKiB:0.0} KiB | {r.ServerHeapPerConnectionKiB:0.0} KiB | {Proxy(r)} | {r.ClientRssPerConnectionKiB:0.0} KiB | {r.Runs} |")).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>The charts, by file name.</summary>
    public static IReadOnlyDictionary<string, string> Charts(IReadOnlyList<StageAggregate> rows, string environment)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var charted = new[] { ScenarioCatalog.SingleNode, ScenarioCatalog.StickyOnly, ScenarioCatalog.StickyRedis, ScenarioCatalog.RoundRobinSkip }
            .Where(s => rows.Any(r => r.Scenario == s.Name))
            .ToList();

        LineSeries Series(Scenario s, Func<StageAggregate, double> y) => new(
            s.Name,
            Slots[s.Name],
            rows.Where(r => r.Scenario == s.Name).Select(r => new ChartPoint(r.Connected, y(r))).ToList());

        var charts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["memory-total.svg"] = SvgChart.Line(
                "Hub server memory vs open connections",
                $"Sum of hub server RSS after a forced full GC, median of runs. {environment}",
                "open connections",
                "RSS, MiB (all hub servers)",
                charted.Select(s => Series(s, r => r.ServerRssMiB)).ToList(),
                yFormat: "#,0"),
            ["memory-per-connection.svg"] = SvgChart.Line(
                "Hub server memory per connection",
                $"(RSS at stage - idle RSS) / connections, summed over hub servers, median of runs. {environment}",
                "open connections",
                "KiB per connection",
                charted.Select(s => Series(s, r => r.ServerRssPerConnectionKiB)).ToList(),
                yFormat: "0"),
            ["latency-p50.svg"] = SvgChart.Line(
                "Broadcast latency, median",
                $"Send request to arrival at a client, p50 over all receives, median of runs. {environment}",
                "open connections",
                "milliseconds",
                charted.Select(s => Series(s, r => r.LatencyP50Ms)).ToList(),
                yFormat: "0.#"),
            ["latency-p99.svg"] = SvgChart.Line(
                "Broadcast latency, p99",
                $"Send request to arrival at a client, p99 over all receives, median of runs. {environment}",
                "open connections",
                "milliseconds",
                charted.Select(s => Series(s, r => r.LatencyP99Ms)).ToList(),
                yFormat: "0.#"),
        };

        // Connect success and delivery per scenario at the largest stage every scenario reached.
        var top = rows.GroupBy(r => r.Scenario).Select(g => g.MaxBy(r => r.Target)!).ToList();
        var ordered = ScenarioCatalog.All.Select(s => top.FirstOrDefault(t => t.Scenario == s.Name)).OfType<StageAggregate>().ToList();
        if (ordered.Count > 0)
        {
            charts["delivery.svg"] = SvgChart.Bars(
                "What each topology delivers",
                ordered.Select(o => o.Target).Distinct().Count() == 1
                    ? string.Create(CultureInfo.InvariantCulture, $"Largest stage: {ordered[0].Target:#,0} connection attempts per scenario, median of runs. {environment}")
                    : $"Largest stage per scenario ({string.Join(", ", ordered.Select(o => string.Create(CultureInfo.InvariantCulture, $"{o.Scenario} {o.Target:#,0}")))} attempts), median of runs. {environment}",
                "percent",
                ordered.Select(o => o.Scenario).ToList(),
                [
                    new BarSeries("connections that opened", 1, ordered.Select(o => Math.Round(o.ConnectRatio * 100, 1)).ToList()),
                    new BarSeries("broadcast receives / connected clients", 3, ordered.Select(o => Math.Round(o.DeliveryRatio * 100, 1)).ToList()),
                ],
                100,
                "0.#");
        }

        return charts;
    }

    private static string Completion(StageAggregate r) =>
        r.DeliveryRatio < 1.0 ? "n/a (never complete)" : string.Create(CultureInfo.InvariantCulture, $"{r.CompletionP50Ms:0.0} ms");

    private static string Proxy(StageAggregate r) =>
        r.ProxyRssPerConnectionKiB is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v:0.0} KiB") : "no proxy";
}
