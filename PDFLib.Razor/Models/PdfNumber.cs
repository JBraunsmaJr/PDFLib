using System.Globalization;

namespace PDFLib.Models;

public class PdfNumber : PdfObject
{
    private readonly string _value;

    public PdfNumber(int value)
    {
        _value = value.ToString();
    }

    public PdfNumber(double value)
    {
        _value = value.ToString(CultureInfo.InvariantCulture);
    }

    public PdfNumber(string value)
    {
        _value = value;
    }

    public override byte[] GetBytes()
    {
        return ToAscii(_value);
    }
}