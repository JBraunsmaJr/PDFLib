namespace PDFLib.Models;

/// <summary>
/// Represents a PDF dictionary object (e.g., &lt;&lt; /Type /Page ... &gt;&gt;).
/// </summary>
public class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _dict = new();

    /// <summary>
    /// Adds or updates a key-value pair in the dictionary.
    /// </summary>
    /// <param name="key">The PDF name key (e.g., "/Type").</param>
    /// <param name="value">The PDF object value.</param>
    public void Add(string key, PdfObject value)
    {
        _dict[key] = value;
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The PDF object associated with the key.</returns>
    public PdfObject Get(string key)
    {
        return _dict[key];
    }

    /// <summary>
    /// Tries to get the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The PDF object if found, otherwise null.</returns>
    public PdfObject? GetOptional(string key)
    {
        return _dict.TryGetValue(key, out var val) ? val : null;
    }

    /// <summary>
    /// Writes the dictionary to the specified binary writer.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    public override void WriteTo(BinaryWriter writer)
    {
        writer.Write(ToAscii("<<\n"));

        foreach (var kvp in _dict)
        {
            writer.Write(ToAscii($"{kvp.Key} "));

            if (kvp.Value.ObjectId.HasValue && !(kvp.Value is PdfReference))
            {
                writer.Write(ToAscii($"{kvp.Value.ObjectId} {kvp.Value.Generation} R\n"));
            }
            else
            {
                kvp.Value.WriteTo(writer);
                writer.Write(ToAscii("\n"));
            }
        }

        writer.Write(ToAscii(">>"));
    }

    /// <summary>
    /// Returns the raw byte representation of the dictionary.
    /// </summary>
    /// <returns>A byte array representing the dictionary.</returns>
    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }
}