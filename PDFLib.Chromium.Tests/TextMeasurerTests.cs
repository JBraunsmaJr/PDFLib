using PDFLib;
using SkiaSharp;

namespace PDFLib.Chromium.Tests;

public class TextMeasurerTests
{
    // The previous (quadratic) implementation, kept as the reference for behaviour
    private static List<string> Reference(string text, float maxWidth, float fontSize)
    {
        using var font = new SKFont(SKTypeface.FromFamilyName("Helvetica")) { Size = fontSize };
        var lines = new List<string>();
        var currentLine = "";
        foreach (var word in text.Split(' '))
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            if (font.MeasureText(testLine) > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else currentLine = testLine;
        }
        if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);
        return lines;
    }

    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog", 80f, 10f)]
    [InlineData("Supercalifragilisticexpialidocious is longer than the line", 40f, 12f)]
    [InlineData("a b c d e f g h i j k l m n o p", 25f, 9f)]
    [InlineData("", 100f, 10f)]
    [InlineData("single", 1f, 10f)]
    public void WrapText_MatchesReferenceImplementation(string text, float width, float size)
        => Assert.Equal(Reference(text, width, size), TextMeasurer.WrapText(text, width, size));

    [Fact]
    public void WrapText_MatchesReference_ForRandomText()
    {
        var rng = new Random(42);
        string[] vocab = ["lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "W", "iii", "invoice", "#42"];
        for (var n = 0; n < 200; n++)
        {
            var text = string.Join(' ', Enumerable.Range(0, rng.Next(1, 80)).Select(_ => vocab[rng.Next(vocab.Length)]));
            var width = rng.Next(30, 400);
            var size = rng.Next(6, 24);
            Assert.Equal(Reference(text, width, size), TextMeasurer.WrapText(text, width, size));
        }
    }

    [Fact]
    public void WrapText_IsThreadSafe()
    {
        var text = string.Join(' ', Enumerable.Repeat("thread safety matters here", 50));
        var expected = new Dictionary<float, List<string>>();
        foreach (var size in new[] { 8f, 14f, 20f }) expected[size] = TextMeasurer.WrapText(text, 200, size);

        Parallel.For(0, 300, i =>
        {
            var size = new[] { 8f, 14f, 20f }[i % 3];
            Assert.Equal(expected[size], TextMeasurer.WrapText(text, 200, size));
        });
    }
}
