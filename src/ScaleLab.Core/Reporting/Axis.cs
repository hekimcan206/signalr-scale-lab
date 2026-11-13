namespace ScaleLab.Core.Reporting;

/// <summary>Axis tick selection.</summary>
public static class Axis
{
    private static readonly double[] Steps = [1, 2, 2.5, 5];

    /// <summary>
    /// "Nice" ticks covering [<paramref name="min"/>, <paramref name="max"/>]: evenly spaced, a step of 1, 2, 2.5 or 5
    /// times a power of ten, the first tick at or below <paramref name="min"/>, the last at or above
    /// <paramref name="max"/>, and at most <paramref name="maxTicks"/> + 1 ticks.
    /// </summary>
    public static IReadOnlyList<double> NiceTicks(double min, double max, int maxTicks = 6)
    {
        if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
        {
            throw new ArgumentException("Axis bounds must be finite numbers.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxTicks, 1);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max - min < 1e-12)
        {
            max = min + (Math.Abs(min) < 1e-12 ? 1 : Math.Abs(min));
        }

        var raw = (max - min) / maxTicks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step;
        double first;
        int count;
        var index = 0;
        while (true)
        {
            // 1, 2, 2.5, 5, 10, 20, 25, 50, 100, ... times the magnitude, until the range fits.
            step = Steps[index % Steps.Length] * magnitude * Math.Pow(10, index / Steps.Length);
            first = Math.Floor(min / step) * step;
            count = (int)Math.Ceiling(((max - first) / step) - 1e-9);
            if (count <= maxTicks)
            {
                break;
            }

            index++;
        }

        var ticks = new double[count + 1];
        for (var i = 0; i <= count; i++)
        {
            // Rounding to the step's precision keeps 0.30000000000000004 out of the labels.
            ticks[i] = Math.Round(first + (i * step), 10);
        }

        return ticks;
    }
}
