using System.IO.Compression;
using PDFLib.Models;
using SkiaSharp;

namespace PDFLib;

public class PdfImage : PdfObject, IDisposable
{
    public int Width { get; }
    public int Height { get; }
    private readonly PdfDictionary _dict = new();
    private readonly byte[]? _data;
    private readonly string? _filePath;
    private readonly string _filter;
    private bool _isTempFile;

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

    public override byte[] GetBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteTo(writer);
        return ms.ToArray();
    }

    public static PdfImage FromFile(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return FromStream(fs);
    }

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
                    var offset = (y * rowSize) + (x * 4);
                    rowData[x * 3] = pixels[offset + 2];     // B -> R
                    rowData[x * 3 + 1] = pixels[offset + 1]; // G -> G
                    rowData[x * 3 + 2] = pixels[offset];     // R -> B
                }
                ds.Write(rowData, 0, rowData.Length);
            }
        }
        
        return new PdfImage(width, height, tempFile, true);
    }

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
}