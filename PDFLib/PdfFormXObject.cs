using System.Text;
using PDFLib.Models;

namespace PDFLib;

public class PdfFormXObject : PdfStreamObject
{
    private readonly MemoryStream _tempStream;
    private readonly StreamWriter _contentWriter;
    private readonly PdfDictionary _resources;
    private readonly PdfDictionary _fonts;

    public PdfFormXObject(int width, int height) : base(new PdfDictionary(), Array.Empty<byte>())
    {
        _tempStream = new MemoryStream();
        _contentWriter = new StreamWriter(_tempStream, Encoding.ASCII);
        _resources = new PdfDictionary();
        _fonts = new PdfDictionary();
        _resources.Add("/Font", _fonts);

        _dict.Add("/Type", new PdfName("/XObject"));
        _dict.Add("/Subtype", new PdfName("/Form"));
        
        var bbox = new PdfArray(
            new PdfNumber(0),
            new PdfNumber(0),
            new PdfNumber(width),
            new PdfNumber(height)
        );
        _dict.Add("/BBox", bbox);
        _dict.Add("/Resources", _resources);
    }

    public void AddFont(string alias, string fontName, PdfDocument doc)
    {
        var fontDict = new PdfDictionary();
        fontDict.Add("/Type", new PdfName("/Font"));
        fontDict.Add("/Subtype", new PdfName("/Type1"));
        fontDict.Add("/BaseFont", new PdfName(fontName));

        var fontId = doc.RegisterObject(fontDict);
        _fonts.Add($"/{alias}", new PdfReference(fontId));
    }

    public void DrawText(string fontAlias, int size, int x, int y, string text)
    {
        _contentWriter.Write($"BT /{fontAlias} {size} Tf {x} {y} Td ({text}) Tj ET\n");
    }

    public void DrawLine(int x1, int y1, int x2, int y2)
    {
        _contentWriter.Write($"{x1} {y1} m {x2} {y2} l S\n");
    }

    public void Build()
    {
        _contentWriter.Flush();
        _content = _tempStream.ToArray();
        _dict.Add("/Length", new PdfNumber(_content.Length));
    }
}
