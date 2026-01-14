using System.Text;

namespace PDFLib.Models;

/// <summary>
/// Base abstract class for all PDF types (e.g., dictionaries, arrays, streams).
/// </summary>
/// <remarks>
/// If the object is "indirect", it is assigned an <see cref="ObjectId"/> and <see cref="Generation"/> number.
/// </remarks>
public abstract class PdfObject
{
    /// <summary>
    /// Gets or sets the unique object ID if this is an indirect object.
    /// </summary>
    public int? ObjectId { get; set; }

    /// <summary>
    /// Gets or sets the generation number (usually 0).
    /// </summary>
    public int Generation { get; set; } = 0;

    /// <summary>
    /// Returns the raw byte representation of the PDF object.
    /// </summary>
    /// <returns>A byte array representing the object.</returns>
    public abstract byte[] GetBytes();

    /// <summary>
    /// Writes the object's byte representation to the specified binary writer.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    public virtual void WriteTo(BinaryWriter writer)
    {
        writer.Write(GetBytes());
    }

    /// <summary>
    /// Converts a string to an ASCII byte array.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>The ASCII byte array.</returns>
    protected byte[] ToAscii(string text)
    {
        return Encoding.ASCII.GetBytes(text);
    }
}