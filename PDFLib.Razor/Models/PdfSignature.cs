namespace PDFLib.Models;

/// <summary>
/// Represents a PDF signature dictionary (/Sig) with placeholders for the actual digital signature and byte range.
/// </summary>
public class PdfSignature : PdfObject
{
    private readonly int _contentsSize;
    private readonly PdfDictionary _dict = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfSignature"/> class.
    /// </summary>
    /// <param name="contentsSize">The size in bytes to reserve for the digital signature.</param>
    public PdfSignature(int contentsSize = 4096)
    {
        _contentsSize = contentsSize;
        _dict.Add("/Type", new PdfName("/Sig"));
        _dict.Add("/Filter", new PdfName("/Adobe.PPKLite"));
        _dict.Add("/SubFilter", new PdfName("/adbe.pkcs7.detached"));
        _dict.Add("/M", new PdfString($"D:{DateTime.Now:yyyyMMddHHmmss}Z"));
    }

    /// <summary>
    /// Gets the absolute offset in the output stream where the /Contents (signature) placeholder begins.
    /// </summary>
    public long ContentsOffset { get; private set; }

    /// <summary>
    /// Gets the absolute offset in the output stream where the /ByteRange placeholder begins.
    /// </summary>
    public long ByteRangeOffset { get; private set; }

    /// <summary>
    /// Writes the signature dictionary to the specified binary writer and captures offsets for signing.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    public override void WriteTo(BinaryWriter writer)
    {
        _dict.Add("/Contents", new PdfPlaceholder(_contentsSize));

        // Space for [0 1234567890 1234567890 1234567890]
        _dict.Add("/ByteRange", new PdfPlaceholder(64));

        /*
            We need to capture the offsets of these placeholders
            However, PdfDictionary.WriteTo doesn't easily let us know where it wrote what.
            Let's refine PdfDictionary or write it manually here.
         */

        writer.Write(ToAscii("<<"));
        foreach (var key in new[] { "/Type", "/Filter", "/SubFilter", "/M" })
        {
            writer.Write(ToAscii($" {key} "));
            _dict.Get(key).WriteTo(writer);
        }

        writer.Write(ToAscii(" /Contents "));
        ContentsOffset = writer.BaseStream.Position;
        new PdfPlaceholder(_contentsSize).WriteTo(writer);

        writer.Write(ToAscii(" /ByteRange "));
        ByteRangeOffset = writer.BaseStream.Position;
        new PdfPlaceholder(64).WriteTo(writer);

        writer.Write(ToAscii(" >>"));
    }

    /// <summary>
    /// Returns the raw byte representation of the signature dictionary.
    /// </summary>
    /// <returns>A byte array representing the signature dictionary.</returns>
    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }
}

internal class PdfPlaceholder : PdfObject
{
    private readonly int _size;

    public PdfPlaceholder(int size)
    {
        _size = size;
    }

    public override void WriteTo(BinaryWriter writer)
    {
        // Likely contents, use < > for hex
        if (_size > 100)
        {
            writer.Write((byte)'<');
            writer.Write(new byte[_size - 2]);
            writer.Write((byte)'>');
        }
        // Likely ByteRange, use [ ]
        else
        {
            writer.Write((byte)'[');
            writer.Write(new byte[_size - 2]);
            writer.Write((byte)']');
        }
    }

    public override byte[] GetBytes()
    {
        var b = new byte[_size];
        b[0] = (byte)'(';
        b[_size - 1] = (byte)')';
        return b;
    }
}