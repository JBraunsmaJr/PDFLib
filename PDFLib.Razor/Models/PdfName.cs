namespace PDFLib.Models;

/// <summary>
///     Represents names such as /Type, /Page, /Font
/// </summary>
public class PdfName : PdfObject
{
    private readonly string _name;

    public PdfName(string name)
    {
        _name = name.StartsWith("/") ? name : "/" + name;
    }

    public override byte[] GetBytes()
    {
        return ToAscii(_name);
    }
}