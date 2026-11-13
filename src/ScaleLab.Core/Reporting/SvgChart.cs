using System.Globalization;
using System.Net;
using System.Text;

namespace ScaleLab.Core.Reporting;

/// <summary>One line on a line chart. <paramref name="Slot"/> is the fixed categorical colour slot (1-5).</summary>
public sealed record LineSeries(string Name, int Slot, IReadOnlyList<ChartPoint> Points);

public readonly record struct ChartPoint(double X, double Y);

/// <summary>One group member on a grouped bar chart, one value per category.</summary>
public sealed record BarSeries(string Name, int Slot, IReadOnlyList<double> Values);

/// <summary>
/// Dependency-free SVG charts for the README and the benchmark report.
/// </summary>
/// <remarks>
/// The output is deterministic (invariant culture, fixed layout), so a chart is a pure function of the measured
/// numbers and is covered by golden tests. Colours are CSS custom properties with a dark-mode block, so the same file
/// reads on a light and a dark GitHub theme. Colour follows the scenario (a fixed slot), never the rank.
/// </remarks>
public static class SvgChart
{
    private const int Width = 760;
    private const int Height = 420;
    private const int Left = 72;
    private const int Right = 200;
    private const int Top = 100;
    private const int Bottom = 56;

    private const string Style =
        "<style>" +
        "svg{--surface:#fcfcfb;--ink:#0b0b0b;--ink2:#52514e;--grid:#e6e5e0;--axis:#a3a29c;" +
        "--s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--s4:#eda100;--s5:#e87ba4}" +
        "@media (prefers-color-scheme:dark){svg{--surface:#1a1a19;--ink:#f0efec;--ink2:#c3c2b7;--grid:#33332f;" +
        "--axis:#6b6a64;--s1:#3987e5;--s2:#d95926;--s3:#199e70;--s4:#c98500;--s5:#d55181}}" +
        "text{font-family:-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;fill:var(--ink2);font-size:12px}" +
        ".t{fill:var(--ink);font-size:16px;font-weight:600}.st{font-size:12px}.lab{fill:var(--ink);font-size:12px}" +
        ".g{stroke:var(--grid);stroke-width:1}.ax{stroke:var(--axis);stroke-width:1}" +
        "</style>";

    /// <summary>Renders a line chart with markers, a legend and direct end labels.</summary>
    public static string Line(
        string title,
        string subtitle,
        string xLabel,
        string yLabel,
        IReadOnlyList<LineSeries> series,
        double? yMin = null,
        double? yMax = null,
        string yFormat = "0.##")
    {
        ArgumentNullException.ThrowIfNull(series);
        var points = series.SelectMany(s => s.Points).ToList();
        if (points.Count == 0)
        {
            throw new ArgumentException("A chart needs at least one point.", nameof(series));
        }

        var xTicks = Axis.NiceTicks(Math.Min(0, points.Min(p => p.X)), points.Max(p => p.X));
        var yTicks = Axis.NiceTicks(yMin ?? Math.Min(0, points.Min(p => p.Y)), yMax ?? points.Max(p => p.Y));
        var plot = new Plot(xTicks[0], xTicks[^1], yTicks[0], yTicks[^1]);

        var sb = Open(title, subtitle);
        Legend(sb, series.Select(s => (s.Name, s.Slot)).ToList());
        Grid(sb, plot, xTicks, yTicks, "#,0", yFormat);
        AxisLabels(sb, xLabel, yLabel);

        var ends = new List<(double Y, string Text, int Slot)>();
        foreach (var s in series)
        {
            if (s.Points.Count == 0)
            {
                continue;
            }

            var ordered = s.Points.OrderBy(p => p.X).ToList();
            var path = string.Join(" ", ordered.Select((p, i) => $"{(i == 0 ? 'M' : 'L')}{F(plot.X(p.X))},{F(plot.Y(p.Y))}"));
            sb.Append(CultureInfo.InvariantCulture, $"<path d=\"{path}\" fill=\"none\" stroke=\"var(--s{s.Slot})\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
            foreach (var p in ordered)
            {
                // Marker with a surface ring, so overlapping markers stay distinguishable.
                sb.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{F(plot.X(p.X))}\" cy=\"{F(plot.Y(p.Y))}\" r=\"4.5\" fill=\"var(--s{s.Slot})\" stroke=\"var(--surface)\" stroke-width=\"2\"><title>{Esc(s.Name)}: {Num(p.Y, yFormat)} at {Num(p.X, "#,0")}</title></circle>");
            }

            var last = ordered[^1];
            ends.Add((plot.Y(last.Y), $"{s.Name} {Num(last.Y, yFormat)}", s.Slot));
        }

        // Direct labels at the line ends, nudged apart so close values do not overprint.
        var placed = SpreadLabels(ends.Select(e => e.Y).ToList(), 15, Top, Height - Bottom);
        for (var i = 0; i < ends.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<text class=\"lab\" x=\"{Width - Right + 10}\" y=\"{F(placed[i] + 4)}\">{Esc(ends[i].Text)}</text>");
        }

        return Close(sb);
    }

    /// <summary>Renders a grouped bar chart with value labels.</summary>
    public static string Bars(
        string title,
        string subtitle,
        string yLabel,
        IReadOnlyList<string> categories,
        IReadOnlyList<BarSeries> series,
        double yMax,
        string valueFormat = "0")
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(series);
        if (categories.Count == 0 || series.Count == 0)
        {
            throw new ArgumentException("A bar chart needs categories and series.");
        }

        var yTicks = Axis.NiceTicks(0, yMax);
        var plotLeft = Left;
        var plotRight = Width - 24;
        var plot = new Plot(0, categories.Count, yTicks[0], yTicks[^1], plotRight, Top + 20);

        var sb = Open(title, subtitle);
        Legend(sb, series.Select(s => (s.Name, s.Slot)).ToList());
        foreach (var t in yTicks)
        {
            var y = plot.Y(t);
            sb.Append(CultureInfo.InvariantCulture, $"<line class=\"g\" x1=\"{plotLeft}\" x2=\"{plotRight}\" y1=\"{F(y)}\" y2=\"{F(y)}\"/>");
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{plotLeft - 8}\" y=\"{F(y + 4)}\" text-anchor=\"end\">{Num(t, valueFormat)}</text>");
        }

        AxisLabels(sb, string.Empty, yLabel);
        var groupWidth = (plotRight - plotLeft) / (double)categories.Count;
        var barWidth = Math.Min(28, (groupWidth * 0.7) / series.Count);
        for (var c = 0; c < categories.Count; c++)
        {
            var groupStart = plotLeft + (c * groupWidth) + ((groupWidth - (barWidth * series.Count) - (2 * (series.Count - 1))) / 2);
            for (var s = 0; s < series.Count; s++)
            {
                var value = series[s].Values[c];
                var x = groupStart + (s * (barWidth + 2));
                var y = plot.Y(value);
                var baseY = plot.Y(0);
                sb.Append(CultureInfo.InvariantCulture, $"<path d=\"{RoundedTop(x, y, barWidth, baseY - y)}\" fill=\"var(--s{series[s].Slot})\"><title>{Esc(categories[c])}, {Esc(series[s].Name)}: {Num(value, valueFormat)}</title></path>");
                sb.Append(CultureInfo.InvariantCulture, $"<text class=\"lab\" x=\"{F(x + (barWidth / 2))}\" y=\"{F(y - 5)}\" text-anchor=\"middle\" font-size=\"11\">{Num(value, valueFormat)}</text>");
            }

            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(plotLeft + (c * groupWidth) + (groupWidth / 2))}\" y=\"{Height - Bottom + 20}\" text-anchor=\"middle\">{Esc(categories[c])}</text>");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<line class=\"ax\" x1=\"{plotLeft}\" x2=\"{plotRight}\" y1=\"{F(plot.Y(0))}\" y2=\"{F(plot.Y(0))}\"/>");
        return Close(sb);
    }

    /// <summary>
    /// Moves label positions apart to at least <paramref name="gap"/> pixels while keeping their order and staying
    /// inside [<paramref name="min"/>, <paramref name="max"/>]. Returns positions in the input order.
    /// </summary>
    internal static IReadOnlyList<double> SpreadLabels(IReadOnlyList<double> desired, double gap, double min, double max)
    {
        var order = desired.Select((y, i) => (y, i)).OrderBy(t => t.y).ThenBy(t => t.i).ToList();
        var placed = new double[desired.Count];
        var previous = double.NegativeInfinity;
        foreach (var (y, i) in order)
        {
            var at = Math.Max(Math.Max(y, min), previous + gap);
            placed[i] = at;
            previous = at;
        }

        // Second pass from the bottom: pull labels that overflowed the band back up, keeping the gap. When the labels
        // fit in the band at all, the top one stays at or below min.
        var next = double.PositiveInfinity;
        for (var k = order.Count - 1; k >= 0; k--)
        {
            var i = order[k].i;
            placed[i] = Math.Min(placed[i], Math.Min(max, next - gap));
            next = placed[i];
        }

        return placed;
    }

    private static StringBuilder Open(string title, string subtitle)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\" role=\"img\" aria-label=\"{Esc(title)}\">");
        sb.Append(Style);
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{Width}\" height=\"{Height}\" fill=\"var(--surface)\"/>");
        sb.Append(CultureInfo.InvariantCulture, $"<text class=\"t\" x=\"{Left - 48}\" y=\"28\">{Esc(title)}</text>");
        var lines = Wrap(subtitle, 118);
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<text class=\"st\" x=\"{Left - 48}\" y=\"{47 + (i * 15)}\">{Esc(lines[i])}</text>");
        }
        return sb;
    }

    /// <summary>Greedy word wrap into at most two lines of about <paramref name="width"/> characters.</summary>
    internal static IReadOnlyList<string> Wrap(string text, int width)
    {
        if (text.Length <= width)
        {
            return [text];
        }

        var cut = text.LastIndexOf(' ', width);
        if (cut <= 0)
        {
            cut = width;
        }

        return [text[..cut].TrimEnd(), text[cut..].Trim()];
    }

    private static string Close(StringBuilder sb) => sb.Append("</svg>\n").ToString();

    private static void Legend(StringBuilder sb, IReadOnlyList<(string Name, int Slot)> items)
    {
        var x = (double)(Left - 48);
        foreach (var (name, slot) in items)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{F(x)}\" y=\"76\" width=\"12\" height=\"12\" rx=\"3\" fill=\"var(--s{slot})\"/>");
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x + 17)}\" y=\"86\">{Esc(name)}</text>");

            // Width estimate for the next entry: about 6.4 px per character at 12 px.
            x += 17 + (name.Length * 6.4) + 18;
        }
    }

    private static void Grid(StringBuilder sb, Plot plot, IReadOnlyList<double> xTicks, IReadOnlyList<double> yTicks, string xFormat, string yFormat)
    {
        foreach (var t in yTicks)
        {
            var y = plot.Y(t);
            sb.Append(CultureInfo.InvariantCulture, $"<line class=\"g\" x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{F(y)}\" y2=\"{F(y)}\"/>");
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{Left - 8}\" y=\"{F(y + 4)}\" text-anchor=\"end\">{Num(t, yFormat)}</text>");
        }

        foreach (var t in xTicks)
        {
            var x = plot.X(t);
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{F(x)}\" y=\"{Height - Bottom + 18}\" text-anchor=\"middle\">{Num(t, xFormat)}</text>");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<line class=\"ax\" x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{F(plot.Y(yTicks[0]))}\" y2=\"{F(plot.Y(yTicks[0]))}\"/>");
    }

    private static void AxisLabels(StringBuilder sb, string xLabel, string yLabel)
    {
        if (xLabel.Length > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{(Left + Width - Right) / 2}\" y=\"{Height - 12}\" text-anchor=\"middle\">{Esc(xLabel)}</text>");
        }

        var cy = (Top + Height - Bottom) / 2;
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"16\" y=\"{cy}\" text-anchor=\"middle\" transform=\"rotate(-90 16 {cy})\">{Esc(yLabel)}</text>");
    }

    private static string RoundedTop(double x, double y, double w, double h)
    {
        var r = Math.Min(4, Math.Min(w / 2, Math.Max(0, h)));
        return $"M{F(x)},{F(y + h)}V{F(y + r)}Q{F(x)},{F(y)} {F(x + r)},{F(y)}H{F(x + w - r)}Q{F(x + w)},{F(y)} {F(x + w)},{F(y + r)}V{F(y + h)}Z";
    }

    private static string F(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    private sealed class Plot(double xMin, double xMax, double yMin, double yMax, double right = Width - Right, double top = Top)
    {
        public double X(double v) => Left + ((v - xMin) / (xMax - xMin) * (right - Left));

        public double Y(double v) => Height - Bottom - ((v - yMin) / (yMax - yMin) * (Height - Bottom - top));
    }
}
