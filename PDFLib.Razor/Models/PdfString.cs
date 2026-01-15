namespace PDFLib.Models;

/// <summary>
/// Represents a PDF string object (e.g., (Hello World)). Includes basic escaping for parentheses and backslashes.
/// </summary>
public class PdfString : PdfObject
{
    private readonly string _text;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfString"/> class.
    /// </summary>
    /// <param name="text">The string text.</param>
    public PdfString(string text)
    {
        _text = text;
    }

    /// <summary>
    /// Returns the raw byte representation of the string, with necessary escaping.
    /// </summary>
    /// <returns>A byte array representing the PDF string.</returns>
    public override byte[] GetBytes()
    {
        var escaped = _text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        return ToAscii($"({escaped})");
    }
}