namespace PDFLib.Models;

/// <summary>
/// Represents a PDF array object (e.g., [1 2 3]).
/// </summary>
public class PdfArray : PdfObject
{
    private readonly List<PdfObject> _items = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfArray"/> class.
    /// </summary>
    /// <param name="initialItems">Optional initial items to add to the array.</param>
    public PdfArray(params PdfObject[] initialItems)
    {
        _items.AddRange(initialItems);
    }

    /// <summary>
    /// Gets the number of items in the array.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Adds an item to the array.
    /// </summary>
    /// <param name="item">The PDF object to add.</param>
    public void Add(PdfObject item)
    {
        _items.Add(item);
    }

    /// <summary>
    /// Writes the array to the specified binary writer.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
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

    /// <summary>
    /// Returns the raw byte representation of the array.
    /// </summary>
    /// <returns>A byte array representing the array.</returns>
    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }
}