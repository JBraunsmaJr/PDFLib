using System.Drawing;
using PDFLib.Models;

namespace PDFLib;

public class PdfImage : PdfStreamObject
{
    public int Width { get; }
    public int Height { get; }

    public PdfImage(int width, int height, byte[] data, string filter = "/FlateDecode") 
        : base(new PdfDictionary(), data)
    {
        Width = width;
        Height = height;

        _dict.Add("/Type", new PdfName("/XObject"));
        _dict.Add("/Subtype", new PdfName("/Image"));
        _dict.Add("/Width", new PdfNumber(width));
        _dict.Add("/Height", new PdfNumber(height));
        _dict.Add("/ColorSpace", new PdfName("/DeviceRGB"));
        _dict.Add("/BitsPerComponent", new PdfNumber(8));
        _dict.Add("/Length", new PdfNumber(data.Length));
        if (!string.IsNullOrEmpty(filter))
            _dict.Add("/Filter", new PdfName(filter));
    }

    public static PdfImage FromFile(string filePath)
    {
        using var image = Image.FromFile(filePath);
        using var bitmap = new Bitmap(image);
        
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rgbData = new byte[width * height * 3];
        int index = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                rgbData[index++] = color.R;
                rgbData[index++] = color.G;
                rgbData[index++] = color.B;
            }
        }

        using var ms = new MemoryStream();
        using (var ds = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal))
        {
            ds.Write(rgbData, 0, rgbData.Length);
        }
        
        return new PdfImage(width, height, ms.ToArray());
    }
}