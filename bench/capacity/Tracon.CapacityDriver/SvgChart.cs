using System.Globalization;
using System.Text;

namespace Tracon.CapacityDriver;

/// <summary>Renders a standalone SVG bar chart of a measured series.</summary>
/// <remarks>
/// <para>
/// Standalone on purpose: no script, no web font, no external reference. The
/// file must still show the same numbers years later, next to the JSON it was
/// computed from.
/// </para>
/// <para>
/// 🚨 A missing step is never drawn as zero. The caller filters out points it
/// could not measure; a bar of height zero would read as "measured, and it was
/// nothing", which is a different claim.
/// </para>
/// </remarks>
public static class SvgChart
{
    /// <summary>Renders one series.</summary>
    /// <param name="title">The chart's title.</param>
    /// <param name="points">The measured points, in order.</param>
    /// <returns>The SVG document.</returns>
    public static string Render(string title, IReadOnlyList<(string Label, double Value)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        const int width = 720;
        const int height = 320;
        const int left = 60;
        const int bottom = 40;
        const int top = 40;

        var plotWidth = width - left - 20;
        var plotHeight = height - top - bottom;
        var maximum = points.Count == 0 ? 1 : Math.Max(1e-9, points.Max(static p => p.Value));

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" role=\"img\">");
        svg.Append(CultureInfo.InvariantCulture, $"<title>{Escape(title)}</title>");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{left}\" y=\"24\" font-family=\"sans-serif\" font-size=\"15\" fill=\"#111827\">{Escape(title)}</text>");
        svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{top + plotHeight}\" stroke=\"#9ca3af\"/>");
        svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{left}\" y1=\"{top + plotHeight}\" x2=\"{left + plotWidth}\" y2=\"{top + plotHeight}\" stroke=\"#9ca3af\"/>");
        svg.Append(CultureInfo.InvariantCulture, $"<text x=\"4\" y=\"{top + 10}\" font-family=\"sans-serif\" font-size=\"11\" fill=\"#374151\">{Number(maximum)}</text>");

        if (points.Count > 0)
        {
            var slot = (double)plotWidth / points.Count;
            var barWidth = Math.Max(4, slot * 0.6);

            for (var index = 0; index < points.Count; index++)
            {
                var (label, value) = points[index];
                var barHeight = Math.Max(1, value / maximum * plotHeight);
                var x = left + (slot * index) + ((slot - barWidth) / 2);
                var y = top + plotHeight - barHeight;

                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{barWidth:0.##}\" height=\"{barHeight:0.##}\" fill=\"#2563eb\"><title>{Escape(label)}: {Number(value)}</title></rect>");
                svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + (barWidth / 2):0.##}\" y=\"{top + plotHeight + 16}\" font-family=\"sans-serif\" font-size=\"11\" fill=\"#374151\" text-anchor=\"middle\">{Escape(label)}</text>");
            }
        }
        else
        {
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{left + 12}\" y=\"{top + (plotHeight / 2)}\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#6b7280\">no measured point</text>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal)
               .Replace("<", "&lt;", StringComparison.Ordinal)
               .Replace(">", "&gt;", StringComparison.Ordinal);
}
