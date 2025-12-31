using System.Text;

namespace PDFLib.Models;

public class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _dict = new();

    public void Add(string key, PdfObject value) => _dict[key] = value;

    public override void WriteTo(BinaryWriter writer)
    {
        writer.Write(ToAscii("<<"));

        foreach (var kvp in _dict)
        {
            writer.Write(ToAscii($" {kvp.Key} "));

            if (kvp.Value.ObjectId.HasValue && !(kvp.Value is PdfReference))
            {
                writer.Write(ToAscii($"{kvp.Value.ObjectId} {kvp.Value.Generation} R\n"));
            }
            else
            {
                kvp.Value.WriteTo(writer);
                if (kvp.Value is PdfDictionary || kvp.Value is PdfArray)
                    writer.Write(ToAscii("\n"));
            }
        }

        writer.Write(ToAscii(" >>"));
    }

    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }
}