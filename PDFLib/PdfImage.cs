using SkiaSharp;
using PDFLib.Models;

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
        using var codec = SKCodec.Create(filePath) ?? throw new Exception("Failed to load image: " + filePath);
        var info = codec.Info;
        var width = info.Width;
        var height = info.Height;

        // Ensure we get RGB888
        var bitmapInfo = new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(bitmapInfo);
        codec.GetPixels(bitmap.Info, bitmap.GetPixels());

        var tempFile = Path.GetTempFileName();
        using (var fs = File.Create(tempFile))
        using (var ds = new System.IO.Compression.ZLibStream(fs, System.IO.Compression.CompressionLevel.Optimal))
        {
            var pixels = bitmap.GetPixelSpan();
            var rowSize = width * 4; // Rgb888x has 4 bytes per pixel

            for (var y = 0; y < height; y++)
            {
                var rowData = new byte[width * 3];
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * rowSize) + (x * 4);
                    rowData[x * 3] = pixels[offset];     // R
                    rowData[x * 3 + 1] = pixels[offset + 1]; // G
                    rowData[x * 3 + 2] = pixels[offset + 2]; // B
                }
                ds.Write(rowData, 0, rowData.Length);
            }
        }
        
        return new PdfImage(width, height, tempFile, true);
    }

    public void Dispose()
    {
        if (_isTempFile && _filePath != null && File.Exists(_filePath))
        {
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
}