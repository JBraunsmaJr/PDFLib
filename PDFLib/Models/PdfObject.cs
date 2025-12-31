using System.Text;

namespace PDFLib.Models;

/// <summary>
/// Base abstract class for all PDF Types
/// </summary>
/// <remarks>
///     If the object is "Indirect", it gets an ID and Gen number (e.g., "1 0 obj")
/// </remarks>
public abstract class PdfObject
{
    public int? ObjectId { get; set; }
    public int Generation { get; set; } = 0;

    public abstract byte[] GetBytes();

    public virtual void WriteTo(BinaryWriter writer)
    {
        writer.Write(GetBytes());
    }
    
    protected byte[] ToAscii(string text) => Encoding.ASCII.GetBytes(text);
}