using SkiaSharp;

namespace PDFLib;

/// <summary>
/// Provides utility methods for measuring and wrapping text using SkiaSharp.
/// </summary>
public static class TextMeasurer
{
    private static readonly SKFont Font = new(SKTypeface.FromFamilyName("Helvetica"));

    /// <summary>
    /// Wraps text to fit within a specified width.
    /// </summary>
    /// <param name="text">The text to wrap.</param>
    /// <param name="maxWidth">The maximum width available for a single line.</param>
    /// <param name="fontSize">The font size to use for measurement.</param>
    /// <returns>A list of strings, each representing a single line of wrapped text.</returns>
    public static List<string> WrapText(string text, float maxWidth, float fontSize)
    {
        Font.Size = fontSize;
        var lines = new List<string>();
        var words = text.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            var width = Font.MeasureText(testLine);

            if (width > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);

        return lines;
    }
}