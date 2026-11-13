namespace ScaleLab.Core.Measurement;

/// <summary>Order statistics used everywhere in the lab. Nearest-rank, so every reported percentile was observed.</summary>
public static class Statistics
{
    /// <summary>
    /// Nearest-rank percentile of an ascending-sorted sample: the smallest value with at least <paramref name="p"/>
    /// percent of the sample at or below it. No interpolation, so a reported p99 is a latency that really happened.
    /// </summary>
    /// <exception cref="ArgumentException">The sample is empty.</exception>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);
        if (sortedAscending.Count == 0)
        {
            throw new ArgumentException("Cannot take a percentile of an empty sample.", nameof(sortedAscending));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(p, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(p, 100);

        var rank = (int)Math.Ceiling(p / 100.0 * sortedAscending.Count);
        return sortedAscending[Math.Clamp(rank - 1, 0, sortedAscending.Count - 1)];
    }

    /// <summary>
    /// Median for aggregating repetitions. With an even count it averages the two middle values, the usual convention
    /// for "median of N runs" (the inputs are already summaries, not raw observations).
    /// </summary>
    public static double Median(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new ArgumentException("Cannot take the median of an empty sample.", nameof(values));
        }

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
