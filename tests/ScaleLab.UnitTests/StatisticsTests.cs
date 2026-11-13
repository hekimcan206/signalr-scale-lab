using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using ScaleLab.Core.Measurement;

namespace ScaleLab.UnitTests;

public sealed class StatisticsTests
{
    [Theory]
    [InlineData(50, 5)]
    [InlineData(90, 9)]
    [InlineData(99, 10)]
    [InlineData(100, 10)]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    public void Nearest_rank_percentile_of_one_to_ten(double p, double expected)
    {
        var sample = Enumerable.Range(1, 10).Select(i => (double)i).ToArray();
        Statistics.Percentile(sample, p).Should().Be(expected);
    }

    [Fact]
    public void Percentile_of_an_empty_sample_is_an_error_not_zero()
    {
        var act = () => Statistics.Percentile([], 50);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    public void Percentile_outside_0_to_100_is_rejected(double p)
    {
        var act = () => Statistics.Percentile([1.0], p);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Median_averages_the_middle_pair_of_an_even_count()
    {
        Statistics.Median([4, 1, 3, 2]).Should().Be(2.5);
        Statistics.Median([5, 1, 3]).Should().Be(3);
        var act = () => Statistics.Median([]);
        act.Should().Throw<ArgumentException>();
    }

    [Property]
    public Property Percentile_is_an_observed_value_and_monotonic_in_p()
    {
        var samples = Gen.Choose(-100_000, 100_000).Select(i => i / 10.0).NonEmptyListOf().Select(l => l.ToArray());
        var ps = Gen.Choose(0, 1000).Select(i => i / 10.0).Two();
        return Prop.ForAll(samples.ToArbitrary(), ps.ToArbitrary(), (sample, pair) =>
        {
            var sorted = sample.Order().ToArray();
            var (lo, hi) = pair.Item1 <= pair.Item2 ? (pair.Item1, pair.Item2) : (pair.Item2, pair.Item1);
            var a = Statistics.Percentile(sorted, lo);
            var b = Statistics.Percentile(sorted, hi);
            return sorted.Contains(a) && sorted.Contains(b) && a <= b;
        });
    }

    [Property]
    public Property At_least_p_percent_of_the_sample_is_at_or_below_the_p_percentile()
    {
        var samples = Gen.Choose(0, 5000).NonEmptyListOf().Select(l => l.Select(i => (double)i).Order().ToArray());
        var p = Gen.Choose(1, 100).Select(i => (double)i);
        return Prop.ForAll(samples.ToArbitrary(), p.ToArbitrary(), (sorted, pct) =>
        {
            var value = Statistics.Percentile(sorted, pct);
            var atOrBelow = sorted.Count(v => v <= value);
            var below = sorted.Count(v => v < value);
            return atOrBelow >= pct / 100.0 * sorted.Length && below < pct / 100.0 * sorted.Length;
        });
    }

    [Fact]
    public void Latency_summary_of_a_known_sample()
    {
        var summary = LatencySummary.From(Enumerable.Range(1, 200).Select(i => (double)i));

        summary.Count.Should().Be(200);
        summary.P50.Should().Be(100);
        summary.P95.Should().Be(190);
        summary.P99.Should().Be(198);
        summary.Max.Should().Be(200);
        summary.Mean.Should().Be(100.5);
        LatencySummary.From([]).Should().Be(LatencySummary.Empty);
    }
}
