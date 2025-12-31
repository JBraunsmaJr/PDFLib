using System.Text;

namespace PDFLib.Models;

public class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _dict = new();

    public void Add(string key, PdfObject value) => _dict[key] = value;

    public override byte[] GetBytes()
    {
        var sb = new StringBuilder("<<");

        foreach (var kvp in _dict)
        {
            sb.Append($" {kvp.Key} ");

            if (kvp.Value.ObjectId.HasValue && !(kvp.Value is PdfReference))
                sb.Append($"{kvp.Value.ObjectId} {kvp.Value.Generation} R\n");
            else
            {
                var bytes = kvp.Value.GetBytes();
                sb.Append(Encoding.ASCII.GetString(bytes));
                if (kvp.Value is PdfDictionary || kvp.Value is PdfArray)
                    sb.Append('\n');
            }
        }

        sb.Append(" >>");
        return ToAscii(sb.ToString());
    }
}