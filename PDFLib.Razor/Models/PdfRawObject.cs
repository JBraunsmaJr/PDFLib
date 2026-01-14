using System.Text;

namespace PDFLib.Models;

/// <summary>
/// Represents a PDF object with raw byte content. Useful for preserving existing objects during incremental updates.
/// </summary>
public class PdfRawObject : PdfObject
{
    private readonly byte[] _rawContent;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRawObject"/> class.
    /// </summary>
    /// <param name="rawContent">The raw byte content of the object.</param>
    public PdfRawObject(byte[] rawContent)
    {
        _rawContent = rawContent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfRawObject"/> class with string content.
    /// </summary>
    /// <param name="content">The string content.</param>
    public PdfRawObject(string content)
    {
        _rawContent = Encoding.ASCII.GetBytes(content);
    }

    /// <summary>
    /// Returns the raw byte content.
    /// </summary>
    /// <returns>A byte array.</returns>
    public override byte[] GetBytes()
    {
        return _rawContent;
    }
}