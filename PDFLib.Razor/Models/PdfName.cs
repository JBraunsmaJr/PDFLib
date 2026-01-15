namespace PDFLib.Models;

/// <summary>
/// Represents a PDF name object (e.g., /Type, /Page, /Font).
/// </summary>
public class PdfName : PdfObject
{
    /// <summary>
    /// Gets the name string, including the leading slash.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfName"/> class.
    /// </summary>
    /// <param name="name">The name string. A leading slash will be added if missing.</param>
    public PdfName(string name)
    {
        Name = name.StartsWith("/") ? name : "/" + name;
    }

    /// <summary>
    /// Returns the raw byte representation of the name.
    /// </summary>
    /// <returns>A byte array representing the name.</returns>
    public override byte[] GetBytes()
    {
        return ToAscii(Name);
    }
}