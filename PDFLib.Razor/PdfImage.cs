using System.IO.Compression;
using PDFLib.Models;
using SkiaSharp;

namespace PDFLib;

/// <summary>
/// Represents a PDF image object (/XObject with /Subtype /Image).
/// Supports loading from stream or file, and basic ZLib compression.
/// </summary>
public class PdfImage : PdfObject, IDisposable
{
    private readonly byte[]? _data;
    private readonly PdfDictionary _dict = new();
    private readonly string? _filePath;
    private readonly string _filter;
    private readonly bool _isTempFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfImage"/> class with raw data.
    /// </summary>
    /// <param name="width">The width of the image in pixels.</param>
    /// <param name="height">The height of the image in pixels.</param>
    /// <param name="data">The raw image data.</param>
    /// <param name="filter">The PDF filter used for the data (e.g., "/FlateDecode").</param>
    public PdfImage(int width, int height, byte[] data, string filter = "/FlateDecode")
    {
        Width = width;
        Height = height;
        _data = data;
        _filter = filter;

        InitializeDict();
        _dict.Add("/Length", new PdfNumber(data.Length));
    }

    private PdfImage(int width, int height, string filePath, bool isTempFile, string filter = "/FlateDecode")
    {
        Width = width;
        Height = height;
        _filePath = filePath;
        _isTempFile = isTempFile;
        _filter = filter;

        InitializeDict();
    }

    /// <summary>
    /// Gets the width of the image.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the image.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Disposes the image resources, including deleting temporary files if applicable.
    /// </summary>
    public void Dispose()
    {
        if (!_isTempFile || _filePath is null || !File.Exists(_filePath)) return;

        try
        {
            File.Delete(_filePath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void InitializeDict()
    {
        _dict.Add("/Type", new PdfName("/XObject"));
        _dict.Add("/Subtype", new PdfName("/Image"));
        _dict.Add("/Width", new PdfNumber(Width));
        _dict.Add("/Height", new PdfNumber(Height));
        _dict.Add("/ColorSpace", new PdfName("/DeviceRGB"));
        _dict.Add("/BitsPerComponent", new PdfNumber(8));
        if (!string.IsNullOrEmpty(_filter))
            _dict.Add("/Filter", new PdfName(_filter));
    }

    /// <summary>
    /// Writes the image object to the specified binary writer.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    public override void WriteTo(BinaryWriter writer)
    {
        if (_data != null)
        {
            _dict.WriteTo(writer);
            writer.Write(ToAscii("\nstream\n"));
            writer.Write(_data);
            writer.Write(ToAscii("\nendstream"));
        }
        else if (_filePath != null)
        {
            var fileInfo = new FileInfo(_filePath);
            _dict.Add("/Length", new PdfNumber(fileInfo.Length));

            _dict.WriteTo(writer);
            writer.Write(ToAscii("\nstream\n"));
            using (var fs = File.OpenRead(_filePath))
            {
                fs.CopyTo(writer.BaseStream);
            }

            writer.Write(ToAscii("\nendstream"));
        }
    }

    /// <summary>
    /// Returns the raw byte representation of the image object.
    /// </summary>
    /// <returns>A byte array representing the image object.</returns>
    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a <see cref="PdfImage"/> from a file.
    /// </summary>
    /// <param name="filePath">The path to the image file.</param>
    /// <returns>A new <see cref="PdfImage"/> instance.</returns>
    public static PdfImage FromFile(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return FromStream(fs);
    }

    /// <summary>
    /// Creates a <see cref="PdfImage"/> from a stream.
    /// </summary>
    /// <param name="stream">The stream containing the image data.</param>
    /// <returns>A new <see cref="PdfImage"/> instance.</returns>
    /// <exception cref="Exception">Thrown if the image cannot be decoded.</exception>
    public static PdfImage FromStream(Stream stream)
    {
        using var bitmap = SKBitmap.Decode(stream) ?? throw new Exception("Failed to load image from stream");
        var width = bitmap.Width;
        var height = bitmap.Height;

        var tempFile = Path.GetTempFileName();
        using (var fs = File.Create(tempFile))
        using (var ds = new ZLibStream(fs, CompressionLevel.Optimal))
        {
            /*
                Ensure we're working with a known format (Rgba8888)
                SKColorType.Rgba888 corresponds to the byte order R, G, B, A.
                If the image appears with swapped colors (e.g. Blue becomes Red),
                it's possible that the platform's SKColorType.N32 is being used elsewhere
                or there's a misunderstanding of the byte order.
                PDF's DeviceRGB expects R, G, B.
             */
            using var converted = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            bitmap.CopyTo(converted);

            var pixels = converted.GetPixelSpan();
            var rowSize = width * 4;

            for (var y = 0; y < height; y++)
            {
                var rowData = new byte[width * 3];
                for (var x = 0; x < width; x++)
                {
                    var offset = y * rowSize + x * 4;
                    rowData[x * 3] = pixels[offset + 2]; // B -> R
                    rowData[x * 3 + 1] = pixels[offset + 1]; // G -> G
                    rowData[x * 3 + 2] = pixels[offset]; // R -> B
                }

                ds.Write(rowData, 0, rowData.Length);
            }
        }

        return new PdfImage(width, height, tempFile, true);
    }
}