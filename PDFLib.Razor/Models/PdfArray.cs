namespace PDFLib.Models;

public class PdfArray : PdfObject
{
    private readonly List<PdfObject> _items = new();

    public PdfArray(params PdfObject[] initialItems)
    {
        _items.AddRange(initialItems);
    }

    public int Count => _items.Count;

    public void Add(PdfObject item)
    {
        _items.Add(item);
    }

    public override void WriteTo(BinaryWriter writer)
    {
        writer.Write(ToAscii("["));
        foreach (var item in _items)
        {
            writer.Write(ToAscii(" "));
            if (item.ObjectId.HasValue && !(item is PdfReference))
                writer.Write(ToAscii($"{item.ObjectId} {item.Generation} R"));
            else
                item.WriteTo(writer);
        }

        writer.Write(ToAscii("]"));
    }

    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }
}