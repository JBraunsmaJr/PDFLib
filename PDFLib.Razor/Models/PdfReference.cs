namespace PDFLib.Models;

/// <summary>
/// Represents an indirect reference to another PDF object (e.g., 1 0 R).
/// </summary>
public class PdfReference : PdfObject
{
    /// <summary>
    /// Gets the object ID being referenced.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReference"/> class.
    /// </summary>
    /// <param name="id">The object ID to reference.</param>
    public PdfReference(int id)
    {
        Id = id;
    }

    /// <summary>
    /// Returns the raw byte representation of the reference.
    /// </summary>
    /// <returns>A byte array representing the reference.</returns>
    public override byte[] GetBytes()
    {
        return ToAscii($"{Id} 0 R");
    }
}