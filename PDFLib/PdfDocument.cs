using System.Text;
using PDFLib.Models;

namespace PDFLib;

public class PdfDocument
{
    private int _nextId = 1;
    private readonly List<PdfObject> _objects = new();
    private readonly PdfDictionary _catalog = new();
    private readonly PdfDictionary _pages = new();
    private readonly PdfArray _kids = new PdfArray();
    private readonly List<long> _offsets = new();

    public PdfDocument()
    {
        _catalog.ObjectId = AssignId();
        _pages.ObjectId = AssignId();

        _catalog.Add("/Type", new PdfName("/Catalog"));
        _catalog.Add("/Pages", _pages);

        _pages.Add("/Type", new PdfName("/Pages"));
        _pages.Add("/Kids", _kids);
        _pages.Add("/Count", new PdfNumber(0));

        _objects.Add(_catalog);
        _objects.Add(_pages);
    }

    private int AssignId() => _nextId++;

    public PdfPage AddPage()
    {
        var catalogId = _catalog.ObjectId ?? throw new InvalidOperationException("Catalog object ID not set");
        var pagesId = _pages.ObjectId ?? throw new InvalidOperationException("Pages object ID not set");
        var page = new PdfPage(this, AssignId(), pagesId);
        _objects.Add(page.PageDict);
        _kids.Add(page.PageDict);
        return page;
    }

    public void Save(string filePath)
    {
        _pages.Add("/Count", new PdfNumber(_kids.Count));

        using var fs = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        writer.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%\u00E2\u00E3\u00E3\u00CF\u00D3\n"));

        var sortedObjects = _objects.Where(x => x.ObjectId.HasValue).OrderBy(x => x.ObjectId).ToList();
        
        foreach (var obj in sortedObjects)
        {
            _offsets.Add(fs.Position);

            var head = $"{obj.ObjectId} 0 obj\n";
            writer.Write(Encoding.ASCII.GetBytes(head));
            writer.Write(obj.GetBytes());
            writer.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }

        var xrefStart = fs.Position;
        writer.Write(Encoding.ASCII.GetBytes("xref\n"));
        writer.Write(Encoding.ASCII.GetBytes($"0 {sortedObjects.Count + 1}\n"));
        writer.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));

        foreach (var offset in _offsets)
            writer.Write(Encoding.ASCII.GetBytes($"{offset:D10} 00000 n \n"));

        writer.Write(Encoding.ASCII.GetBytes("trailer\n"));
        var trailer = new PdfDictionary();
        trailer.Add("/Size", new PdfNumber(sortedObjects.Count + 1));
        var catalogId = _catalog.ObjectId ?? throw new InvalidOperationException("Catalog object ID not set");
        trailer.Add("/Root", new PdfReference(catalogId));

        writer.Write(trailer.GetBytes());
        writer.Write(Encoding.ASCII.GetBytes($"\nstartxref\n{xrefStart}\n%%EOF"));
    }

    public int RegisterObject(PdfObject obj)
    {
        obj.ObjectId = AssignId();
        _objects.Add(obj);
        return obj.ObjectId.Value;
    }
}