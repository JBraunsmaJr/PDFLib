using System.Text;

namespace PDFLib.Models;

public class PdfArray : PdfObject
{
    private readonly List<PdfObject> _items = new();

    public int Count => _items.Count;
    public void Add(PdfObject item) => _items.Add(item);

    public override byte[] GetBytes()
    {
        var sb = new StringBuilder("[");
        foreach (var item in _items)
        {
            sb.Append(' ');
            if (item.ObjectId.HasValue && !(item is PdfReference))
                sb.Append($"{item.ObjectId} {item.Generation} R");
            else
                sb.Append(Encoding.ASCII.GetString(item.GetBytes()));
        }

        sb.Append(']');
        return ToAscii(sb.ToString());
    }
}