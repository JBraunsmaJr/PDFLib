using SkiaSharp;

namespace PDFLib;

public static class TextMeasurer
{
    private static readonly SKFont Font = new(SKTypeface.FromFamilyName("Helvetica"));

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

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines;
    }
}
