using System.Runtime.CompilerServices;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using ScaleLab.Core.Measurement;
using ScaleLab.Core.Reporting;

namespace ScaleLab.UnitTests;

public sealed class AxisTests
{
    [Theory]
    [InlineData(0, 10000, new double[] { 0, 2000, 4000, 6000, 8000, 10000 })]
    [InlineData(0, 1, new double[] { 0, 0.2, 0.4, 0.6, 0.8, 1 })]
    [InlineData(0, 37, new double[] { 0, 10, 20, 30, 40 })]
    [InlineData(5, 5, new double[] { 5, 6, 7, 8, 9, 10 })]
    public void Known_ranges_get_round_ticks(double min, double max, double[] expected)
    {
        Axis.NiceTicks(min, max).Should().Equal(expected);
    }

    [Property]
    public Property Ticks_cover_the_range_evenly_with_a_round_step()
    {
        var gen = Gen.Choose(-1_000_000, 1_000_000).Two().Select(t => (t.Item1 / 100.0, t.Item2 / 100.0));
        var maxTicks = Gen.Choose(2, 10);
        return Prop.ForAll(gen.ToArbitrary(), maxTicks.ToArbitrary(), (range, n) =>
        {
            var (a, b) = range;
            var (lo, hi) = a <= b ? (a, b) : (b, a);
            var ticks = Axis.NiceTicks(lo, hi, n);
            var step = ticks[1] - ticks[0];
            var mantissa = step / Math.Pow(10, Math.Floor(Math.Log10(step)));
            var round = new[] { 1, 2, 2.5, 5 }.Any(m => Math.Abs(mantissa - m) < 1e-6);
            var even = ticks.Zip(ticks.Skip(1), (x, y) => y - x).All(d => Math.Abs(d - step) < step * 1e-6);
            return ticks[0] <= lo + 1e-9 && ticks[^1] >= hi - 1e-9 && ticks.Count <= n + 1 && round && even;
        });
    }

    [Fact]
    public void Non_finite_bounds_are_rejected()
    {
        var act = () => Axis.NiceTicks(0, double.NaN);
        act.Should().Throw<ArgumentException>();
    }
}

public sealed class SvgChartTests
{
    [Property]
    public Property Spread_labels_keep_order_and_minimum_gap_inside_the_band()
    {
        var gen = Gen.Choose(0, 300).ListOf().Select(l => l.Select(i => (double)i).ToList());
        return Prop.ForAll(gen.ToArbitrary(), desired =>
        {
            if (desired.Count > 15)
            {
                desired = desired.Take(15).ToList();
            }

            var placed = SvgChart.SpreadLabels(desired, 15, 0, 300);
            var order = desired.Select((y, i) => (y, i)).OrderBy(t => t.y).ThenBy(t => t.i).Select(t => placed[t.i]).ToList();
            var gapsOk = order.Zip(order.Skip(1), (p, q) => q - p).All(g => g >= 15 - 1e-9);
            return gapsOk && placed.All(p => p >= 0 && p <= 300 + 1e-9);
        });
    }

    [Fact]
    public void Chart_output_is_identical_whatever_the_current_culture()
    {
        var series = new[] { new LineSeries("a", 1, [new ChartPoint(1000, 1.5), new ChartPoint(2000, 2.25)]) };
        var invariant = SvgChart.Line("t", "s", "x", "y", series);
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            SvgChart.Line("t", "s", "x", "y", series).Should().Be(invariant);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }

        invariant.Should().Contain("2.25").And.NotContain("2,25");
    }

    [Fact]
    public void Labels_are_escaped()
    {
        var svg = SvgChart.Line("<script>", "a & b", "x", "y", [new LineSeries("\"q\"", 1, [new ChartPoint(1, 1)])]);
        svg.Should().NotContain("<script>").And.Contain("&lt;script&gt;").And.Contain("a &amp; b");
    }

    [Fact]
    public void Empty_input_is_rejected()
    {
        var line = () => SvgChart.Line("t", "s", "x", "y", [new LineSeries("a", 1, [])]);
        var bars = () => SvgChart.Bars("t", "s", "y", [], [], 100);
        line.Should().Throw<ArgumentException>();
        bars.Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// Golden tests: the markdown table and the charts for a fixed synthetic set of runs. Regenerate with
/// <c>UPDATE_GOLDEN=1 dotnet test --project tests/ScaleLab.UnitTests</c> and review the diff like code.
/// </summary>
public sealed class ReportGoldenTests
{
    [Fact]
    public void Aggregation_takes_medians_and_charges_memory_growth_per_connection()
    {
        var rows = Aggregation.Aggregate(SyntheticRuns.All());

        var sticky = rows.Single(r => r.Scenario == "sticky-redis" && r.Target == 2000);
        sticky.Runs.Should().Be(3);

        // Server RSS grows by 20, 30, 40 MiB over the baseline at 2,000 connections: median 30 MiB = 15,360 KiB.
        sticky.ServerRssPerConnectionKiB.Should().BeApproximately(30 * 1024.0 / 2000, 1e-9);
        sticky.LatencyP50Ms.Should().Be(6);
        sticky.DeliveryRatio.Should().Be(1.0);
        sticky.ProxyRssPerConnectionKiB.Should().NotBeNull();

        var single = rows.Single(r => r.Scenario == "single" && r.Target == 2000);
        single.ProxyRssPerConnectionKiB.Should().BeNull("the single-node scenario has no proxy in the path");

        var rr = rows.Single(r => r.Scenario == "rr-negotiate" && r.Target == 2000);
        rr.ConnectRatio.Should().BeApproximately(0.34, 1e-9);

        rows.Select(r => r.Scenario).Distinct().Should().Equal("single", "sticky-only", "sticky-redis", "rr-negotiate", "rr-skip");
    }

    [Fact]
    public void Markdown_table_matches_the_approved_file()
    {
        Golden.Verify("summary.approved.md", Report.Markdown(Aggregation.Aggregate(SyntheticRuns.All())));
    }

    [Theory]
    [InlineData("memory-total.svg")]
    [InlineData("memory-per-connection.svg")]
    [InlineData("latency-p50.svg")]
    [InlineData("latency-p99.svg")]
    [InlineData("delivery.svg")]
    public void Charts_match_the_approved_files(string name)
    {
        var charts = Report.Charts(Aggregation.Aggregate(SyntheticRuns.All()), "Synthetic test data.");
        Golden.Verify(name.Replace(".svg", ".approved.svg", StringComparison.Ordinal), charts[name]);
    }
}

internal static class SyntheticRuns
{
    private const long MiB = 1024 * 1024;
    private static readonly int[] Targets = [1000, 2000];

    public static IEnumerable<RunResult> All()
    {
        var reps = new[] { (Rep: 1, Grow: 20L, P50: 5.0), (Rep: 2, Grow: 30L, P50: 6.0), (Rep: 3, Grow: 40L, P50: 9.0) };
        foreach (var (name, delivery, connect, latencyFactor) in new[]
        {
            ("single", 1.0, 1.0, 1.0),
            ("sticky-only", 0.334, 1.0, 1.1),
            ("sticky-redis", 1.0, 1.0, 1.0),
            ("rr-negotiate", 1.0, 0.34, 1.0),
            ("rr-skip", 1.0, 1.0, 1.05),
        })
        {
            foreach (var (rep, grow, p50) in reps)
            {
                var stages = Targets.Select(target =>
                {
                    var connected = (int)(target * connect);
                    var memory = Snapshot(100 * MiB + (grow * MiB * target / 2000), 40 * MiB + (grow * MiB * target / 4000));
                    var fanout = new FanoutSummary(
                        10,
                        delivery >= 1 ? 10 : 0,
                        connected * 10L,
                        (long)(connected * 10L * delivery),
                        delivery,
                        new LatencySummary(connected * 10, p50 * latencyFactor * target / 2000, 0, 3 * p50 * latencyFactor * target / 2000, 0, 0),
                        delivery >= 1 ? new LatencySummary(10, 2 * p50 * target / 2000, 0, 0, 0, 0) : LatencySummary.Empty,
                        0);
                    return new StageResult(target, connected, target - connected, 0, new Dictionary<string, int>(), 1, new Dictionary<string, int>(), memory, fanout);
                }).ToList();
                yield return new RunResult(
                    name, rep, DateTimeOffset.UnixEpoch, new RunSettings(3, 10, 2, 32, 100, "synthetic"), Snapshot(100 * MiB, 40 * MiB), stages);
            }
        }
    }

    private static MemorySnapshot Snapshot(long serverRss, long serverHeap) =>
        new(serverRss, serverHeap, 60 * MiB + (serverRss / 10), 20 * MiB, 200 * MiB + serverRss, 80 * MiB, []);
}

internal static class Golden
{
    public static void Verify(string fileName, string actual, [CallerFilePath] string callerPath = "")
    {
        var sourceDir = Path.Combine(Path.GetDirectoryName(callerPath)!, "Golden");
        var sourceFile = Path.Combine(sourceDir, fileName);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(sourceFile, actual);
        }

        var approvedPath = Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
        File.Exists(approvedPath).Should().BeTrue($"approved file {fileName} must exist (run with UPDATE_GOLDEN=1 to create it)");
        var approved = File.ReadAllText(approvedPath).ReplaceLineEndings("\n");
        actual.ReplaceLineEndings("\n").Should().Be(approved, $"output must match Golden/{fileName}");
    }
}
