namespace PDFLib.Models;

public class PdfReference : PdfObject
{
    private readonly int _id;

    public PdfReference(int id)
    {
        _id = id;
    }

    public override byte[] GetBytes()
    {
        return ToAscii($"{_id} 0 R");
    }
}