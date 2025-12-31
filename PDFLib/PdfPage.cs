using System.Text;
using PDFLib.Models;

namespace PDFLib;

public class PdfPage
{
    public PdfDictionary PageDict { get; }
    private readonly PdfDocument _doc;
    private readonly Stream _tempStream;
    private readonly StreamWriter _contentWriter;
    private readonly PdfDictionary _resources;
    private readonly PdfDictionary _fonts;
    private readonly PdfDictionary _xobjects;

    public PdfPage(PdfDocument doc, int pageId, int parentId)
    {
        _doc = doc;
        _tempStream = new MemoryStream();
        _contentWriter = new StreamWriter(_tempStream, Encoding.ASCII);
        _resources = new PdfDictionary();
        _fonts = new PdfDictionary();
        _xobjects = new PdfDictionary();
        _resources.Add("/Font", _fonts);
        _resources.Add("/XObject", _xobjects);

        PageDict = new PdfDictionary();
        PageDict.ObjectId = pageId;
        PageDict.Add("/Type", new PdfName("/Page"));
        PageDict.Add("/Parent", new PdfReference(parentId));

        var mediaBox = new PdfArray();
        string[] a4 = ["0", "0", "595", "842"];

        foreach (var i in a4)
            mediaBox.Add(new PdfNumber(i));
        
        PageDict.Add("/MediaBox", mediaBox);
        PageDict.Add("/Resources", _resources);
    }

    public void AddFont(string alias, string fontName)
    {
        var fontDict = new PdfDictionary();
        fontDict.Add("/Type", new PdfName("/Font"));
        fontDict.Add("/Subtype", new PdfName("/Type1"));
        fontDict.Add("/BaseFont", new PdfName(fontName));

        var fontId = _doc.RegisterObject(fontDict);
        _fonts.Add("/" + alias, new PdfReference(fontId));
    }

    public void DrawText(string fontAlias, int size, int x, int y, string text)
    {
        _contentWriter.Write($"BT /{fontAlias} {size} Tf {x} {y} Td ({text}) Tj ET\n");
    }

    public void DrawLine(int x1, int y1, int x2, int y2)
    {
        _contentWriter.Write($"{x1} {y1} m {x2} {y2} l S\n");
    }

    public void DrawImage(string alias, PdfImage image, int x, int y, int width, int height)
    {
        if (image.ObjectId == null)
            _doc.RegisterObject(image);

        if (image.ObjectId.HasValue)
        {
            _xobjects.Add("/" + alias, new PdfReference(image.ObjectId.Value));
            _contentWriter.Write($"q {width} 0 0 {height} {x} {y} cm /{alias} Do Q\n");
        }
    }

    public void DrawTable(PdfTable table, int x, int y)
    {
        table.Render(this, x, y);
    }

    public void Build(bool compress = false)
    {
        _contentWriter.Flush();
        _tempStream.Seek(0, SeekOrigin.Begin);
        
        var streamDict = new PdfDictionary();
        byte[] finalContent;

        if (compress)
        {
            using var ms = new MemoryStream();
            using (var ds = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal))
            {
                _tempStream.CopyTo(ds);
            }
            finalContent = ms.ToArray();
            streamDict.Add("/Filter", new PdfName("/FlateDecode"));
        }
        else
        {
            using var ms = new MemoryStream();
            _tempStream.CopyTo(ms);
            finalContent = ms.ToArray();
        }

        streamDict.Add("/Length", new PdfNumber(finalContent.Length));

        var streamObj = new PdfStreamObject(streamDict, finalContent);
        var streamId = _doc.RegisterObject(streamObj);
        PageDict.Add("/Contents", new PdfReference(streamId));
        
        _doc.WriteObject(PageDict);
    }
}