namespace ScaleLab.Core.Measurement;

/// <summary>Distribution of a set of latencies in milliseconds.</summary>
public sealed record LatencySummary(int Count, double P50, double P95, double P99, double Max, double Mean)
{
    public static LatencySummary Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public static LatencySummary From(IEnumerable<double> millis)
    {
        ArgumentNullException.ThrowIfNull(millis);
        var sorted = millis.Order().ToArray();
        if (sorted.Length == 0)
        {
            return Empty;
        }

        return new LatencySummary(
            sorted.Length,
            Statistics.Percentile(sorted, 50),
            Statistics.Percentile(sorted, 95),
            Statistics.Percentile(sorted, 99),
            sorted[^1],
            sorted.Average());
    }
}
