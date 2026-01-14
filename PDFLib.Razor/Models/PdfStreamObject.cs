using System.Text;

namespace PDFLib.Models;

/// <summary>
/// Represents a PDF stream object, consisting of a dictionary followed by raw data.
/// </summary>
public class PdfStreamObject : PdfObject
{
    /// <summary>
    /// The dictionary associated with the stream.
    /// </summary>
    protected readonly PdfDictionary _dict;

    /// <summary>
    /// The raw content of the stream.
    /// </summary>
    protected byte[] _content;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStreamObject"/> class with string content.
    /// </summary>
    /// <param name="dict">The stream dictionary.</param>
    /// <param name="content">The string content, which will be converted to ASCII bytes.</param>
    public PdfStreamObject(PdfDictionary dict, string content) : this(dict, Encoding.ASCII.GetBytes(content))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStreamObject"/> class with byte array content.
    /// </summary>
    /// <param name="dict">The stream dictionary.</param>
    /// <param name="content">The raw byte content.</param>
    public PdfStreamObject(PdfDictionary dict, byte[] content)
    {
        _dict = dict;
        _content = content;
    }

    /// <summary>
    /// Writes the stream object to the specified binary writer.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    public override void WriteTo(BinaryWriter writer)
    {
        _dict.WriteTo(writer);
        writer.Write(ToAscii("\nstream\n"));
        writer.Write(_content);
        writer.Write(ToAscii("\nendstream"));
    }

    /// <summary>
    /// Returns the raw byte representation of the stream object.
    /// </summary>
    /// <returns>A byte array representing the stream object.</returns>
    public override byte[] GetBytes()
    {
        var bytes = new List<byte>();
        bytes.AddRange(_dict.GetBytes());
        bytes.AddRange(ToAscii("\nstream\n"));
        bytes.AddRange(_content);
        bytes.AddRange(ToAscii("\nendstream"));
        return bytes.ToArray();
    }
}