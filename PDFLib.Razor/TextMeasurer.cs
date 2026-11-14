using SkiaSharp;

namespace PDFLib;

/// <summary>
/// Provides utility methods for measuring and wrapping text using SkiaSharp.
/// </summary>
public static class TextMeasurer
{
    private static readonly SKFont Font = new(SKTypeface.FromFamilyName("Helvetica"));

    // SKFont is mutable (Size) and not thread-safe; renders can run concurrently.
    private static readonly object FontLock = new();

    /// <summary>
    /// Wraps text to fit within a specified width.
    /// </summary>
    /// <param name="text">The text to wrap.</param>
    /// <param name="maxWidth">The maximum width available for a single line.</param>
    /// <param name="fontSize">The font size to use for measurement.</param>
    /// <returns>A list of strings, each representing a single line of wrapped text.</returns>
    public static List<string> WrapText(string text, float maxWidth, float fontSize)
    {
        var lines = new List<string>();
        var words = text.Split(' ');

        lock (FontLock)
        {
            Font.Size = fontSize;

            /*
             * Measure each word once and track the running line width, instead of re-measuring the
             * whole growing line for every word (quadratic in line length, plus a string allocation each time).
             * Advances are additive for this font (no shaping/kerning), so widths match whole-line measurement.
             */
            var spaceWidth = Font.MeasureText(" ");
            var line = new System.Text.StringBuilder();
            var lineWidth = 0f;

            foreach (var word in words)
            {
                var wordWidth = Font.MeasureText(word);

                if (line.Length == 0)
                {
                    line.Append(word);
                    lineWidth = wordWidth;
                    continue;
                }

                var testWidth = lineWidth + spaceWidth + wordWidth;
                if (testWidth > maxWidth)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                    lineWidth = wordWidth;
                }
                else
                {
                    line.Append(' ').Append(word);
                    lineWidth = testWidth;
                }
            }

            if (line.Length > 0) lines.Add(line.ToString());
        }

        return lines;
    }
}