namespace PDFLib.Models;

/// <summary>
///     Basic escaping included
/// </summary>
public class PdfString : PdfObject
{
    private readonly string _text;

    public PdfString(string text)
    {
        _text = text;
    }

    public override byte[] GetBytes()
    {
        var escaped = _text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        return ToAscii($"({escaped})");
    }
}