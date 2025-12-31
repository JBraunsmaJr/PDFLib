using System.Text;
using PDFLib.Models;

namespace PDFLib;

public class PdfDocument : IDisposable
{
    private int _nextId = 1;
    private readonly List<PdfObject> _objects = new();
    private readonly PdfDictionary _catalog = new();
    private readonly PdfDictionary _pages = new();
    private readonly PdfArray _kids = new PdfArray();
    private readonly Dictionary<int, long> _offsets = new();
    private Stream? _outputStream;
    private BinaryWriter? _writer;
    private bool _isStreaming;

    public PdfDocument()
    {
    }

    public void Begin(Stream outputStream)
    {
        _outputStream = outputStream;
        _writer = new BinaryWriter(_outputStream, Encoding.ASCII, leaveOpen: true);
        _isStreaming = true;

        _writer.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n%\u00E2\u00E3\u00E3\u00CF\u00D3\n"));

        _catalog.ObjectId = AssignId();
        _pages.ObjectId = AssignId();

        _catalog.Add("/Type", new PdfName("/Catalog"));
        _catalog.Add("/Pages", _pages);

        _pages.Add("/Type", new PdfName("/Pages"));
        _pages.Add("/Kids", _kids);
        // /Count will be updated at the end
    }

    private int AssignId() => _nextId++;

    public PdfPage AddPage()
    {
        if (!_isStreaming) throw new InvalidOperationException("Call Begin() first");
        
        var pagesId = _pages.ObjectId ?? throw new InvalidOperationException("Pages object ID not set");
        var page = new PdfPage(this, AssignId(), pagesId);
        _kids.Add(new PdfReference(page.PageDict.ObjectId!.Value));
        return page;
    }

    public int RegisterObject(PdfObject obj)
    {
        obj.ObjectId = AssignId();
        if (_isStreaming)
        {
            WriteObject(obj);
        }
        else
        {
            _objects.Add(obj);
        }
        return obj.ObjectId.Value;
    }

    public void WriteObject(PdfObject obj)
    {
        if (!_isStreaming || _writer == null || _outputStream == null)
            throw new InvalidOperationException("Document is not in streaming mode");

        var id = obj.ObjectId ?? throw new ArgumentException("Object must have an ID");
        _offsets[id] = _outputStream.Position;

        var head = $"{id} 0 obj\n";
        _writer.Write(Encoding.ASCII.GetBytes(head));
        obj.WriteTo(_writer);
        _writer.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
    }

    public void Close()
    {
        if (!_isStreaming || _writer == null || _outputStream == null) return;

        // Write Catalog and Pages objects if they weren't written yet
        // In this implementation, we write them at the end for simplicity in tracking _kids and _count
        _pages.Add("/Count", new PdfNumber(_kids.Count));
        
        WriteObject(_catalog);
        WriteObject(_pages);

        var sortedIds = _offsets.Keys.OrderBy(x => x).ToList();
        var maxId = sortedIds.LastOrDefault();

        var xrefStart = _outputStream.Position;
        _writer.Write(Encoding.ASCII.GetBytes("xref\n"));
        _writer.Write(Encoding.ASCII.GetBytes($"0 {maxId + 1}\n"));
        _writer.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));

        for (var i = 1; i <= maxId; i++)
        {
            if (_offsets.TryGetValue(i, out var offset))
            {
                _writer.Write(Encoding.ASCII.GetBytes($"{offset:D10} 00000 n \n"));
            }
            else
            {
                _writer.Write(Encoding.ASCII.GetBytes("0000000000 00000 f \n"));
            }
        }

        _writer.Write(Encoding.ASCII.GetBytes("trailer\n"));
        var trailer = new PdfDictionary();
        trailer.Add("/Size", new PdfNumber(maxId + 1));
        trailer.Add("/Root", new PdfReference(_catalog.ObjectId!.Value));

        _writer.Write(trailer.GetBytes());
        _writer.Write(Encoding.ASCII.GetBytes($"\nstartxref\n{xrefStart}\n%%EOF"));
        
        _writer.Flush();
        _isStreaming = false;
    }

    public void Save(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Create);
        Begin(fs);
        
        // For simple usage where Build wasn't called on pages, 
        // we might still have objects in _objects list if RegisterObject was called before Begin
        // or in some other scenarios. 
        // But with the current logic, everything should be written via WriteObject.
        
        Close();
    }

    public void Dispose()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Ignore if stream already closed
        }
    }
}