using System.Text;
using System.Security.Cryptography.X509Certificates;
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
    private PdfSignature? _signature;
    private X509Certificate2? _certificate;
    private PdfArray? _sigRect;

    public PdfDocument()
    {
    }

    private PdfDictionary? _signatureField;
    private PdfFormXObject? _signatureAppearance;

    public void AddSignature(X509Certificate2 certificate, int? x = null, int? y = null, int? width = null, int? height = null)
    {
        _certificate = certificate;
        _signature = new PdfSignature();

        if (x.HasValue && y.HasValue && width.HasValue && height.HasValue)
        {
            _sigRect = new PdfArray();
            _sigRect.Add(new PdfNumber(x.Value));
            _sigRect.Add(new PdfNumber(y.Value));
            _sigRect.Add(new PdfNumber(x.Value + width.Value));
            _sigRect.Add(new PdfNumber(y.Value + height.Value));

            // Create appearance
            _signatureAppearance = new PdfFormXObject(width.Value, height.Value);
            _signatureAppearance.AddFont("F1", "Helvetica", this);
            
            var name = certificate.GetNameInfo(X509NameType.SimpleName, false) ?? certificate.Subject;
            _signatureAppearance.DrawText("F1", 10, 2, height.Value - 12, $"Digitally signed by: {name}");
            _signatureAppearance.DrawText("F1", 8, 2, height.Value - 24, $"Date: {DateTime.Now:yyyy.MM.dd HH:mm:ss zzz}");
            _signatureAppearance.DrawLine(0, 0, width.Value, 0);
            _signatureAppearance.DrawLine(0, 0, 0, height.Value);
            _signatureAppearance.DrawLine(width.Value, 0, width.Value, height.Value);
            _signatureAppearance.DrawLine(0, height.Value, width.Value, height.Value);
            _signatureAppearance.Build();
        }
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

        if (_signature != null)
        {
            var acroForm = new PdfDictionary();
            var fields = new PdfArray();
            acroForm.Add("/Fields", fields);
            acroForm.Add("/SigFlags", new PdfNumber(3)); // 1 (SignaturesExist) + 2 (AppendOnly)
            _catalog.Add("/AcroForm", acroForm);

            var sigField = new PdfDictionary();
            sigField.Add("/Type", new PdfName("/Annot"));
            sigField.Add("/Subtype", new PdfName("/Widget"));
            sigField.Add("/FT", new PdfName("/Sig"));
            sigField.Add("/T", new PdfString("Signature1"));
            sigField.Add("/V", _signature);
            sigField.Add("/Rect", _sigRect ?? new PdfArray());
            sigField.Add("/F", new PdfNumber(4)); // Print flag

            if (_signatureAppearance != null)
            {
                var ap = new PdfDictionary();
                ap.Add("/N", _signatureAppearance);
                sigField.Add("/AP", ap);
                RegisterObject(_signatureAppearance);
            }

            fields.Add(sigField);
            
            if (_sigRect != null)
            {
                // To make it visible, it must be in a page's /Annots
                // We'll store it and add it to the first page when it's created
                _signatureField = sigField;
            }
        }
    }

    private int AssignId() => _nextId++;

    public PdfPage AddPage()
    {
        if (!_isStreaming) throw new InvalidOperationException("Call Begin() first");
        
        var pagesId = _pages.ObjectId ?? throw new InvalidOperationException("Pages object ID not set");
        var page = new PdfPage(this, AssignId(), pagesId);
        _kids.Add(new PdfReference(page.PageDict.ObjectId!.Value));

        if (_signatureField != null)
        {
            var annots = new PdfArray();
            annots.Add(_signatureField);
            page.PageDict.Add("/Annots", annots);
            _signatureField = null; // Only add to the first page
        }

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

        if (_signature != null)
        {
            RegisterObject(_signature);
        }

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

        if (_signature != null && _certificate != null)
        {
            SignDocument();
        }

        _isStreaming = false;
    }

    private void SignDocument()
    {
        if (_signature == null || _certificate == null || _outputStream == null) return;

        var fileLength = _outputStream.Position;
        
        /*
            Calculate ByteRange
            [0 offset1 offset2 offset3]
            
            offset1 = byte count before /Contents <
            offset2 = byte count after > until the end of /Contents entry
            standard ByteRange is [0 PART1_LENGTH PART2_START PART2_LENGTH]
         */
        
        var contentsStart = _signature.ContentsOffset;
        
        // This should match _contentsSize in PdfSignature
        var contentsLength = 4096; 

        var part1Length = (int)contentsStart;
        var part2Start = (int)(contentsStart + contentsLength);
        var part2Length = (int)(fileLength - part2Start);

        var byteRange = $"[0 {part1Length} {part2Start} {part2Length}]";
        
        _outputStream.Seek(_signature.ByteRangeOffset, SeekOrigin.Begin);
        var byteRangeBytes = Encoding.ASCII.GetBytes(byteRange.PadRight(64));
        _outputStream.Write(byteRangeBytes, 0, byteRangeBytes.Length);

        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            _outputStream.Seek(0, SeekOrigin.Begin);
            
            // We need to hash part1 and part2
            var buffer = new byte[8192];
            int read;
            long totalRead = 0;
            
            // Hash Part 1
            while (totalRead < part1Length)
            {
                read = _outputStream.Read(buffer, 0, (int)Math.Min(buffer.Length, part1Length - totalRead));
                if (read == 0) break;
                sha.TransformBlock(buffer, 0, read, null, 0);
                totalRead += read;
            }

            // Hash Part 2
            _outputStream.Seek(part2Start, SeekOrigin.Begin);
            totalRead = 0;
            while (totalRead < part2Length)
            {
                read = _outputStream.Read(buffer, 0, (int)Math.Min(buffer.Length, part2Length - totalRead));
                if (read == 0) break;
                sha.TransformBlock(buffer, 0, read, null, 0);
                totalRead += read;
            }
            
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            hash = sha.Hash!;
        }

        // Sign
        var contentInfo = new System.Security.Cryptography.Pkcs.ContentInfo(hash);
        var signedCms = new System.Security.Cryptography.Pkcs.SignedCms(contentInfo, detached: true);
        var cmsSigner = new System.Security.Cryptography.Pkcs.CmsSigner(_certificate)
        {
            DigestAlgorithm = new System.Security.Cryptography.Oid("2.16.840.1.101.3.4.2.1") // SHA256
        };

        signedCms.ComputeSignature(cmsSigner);
        var signatureBytes = signedCms.Encode();

        if (signatureBytes.Length * 2 > contentsLength - 2)
            throw new Exception("Signature too large for reserved space");

        // Write Signature to /Contents
        _outputStream.Seek(contentsStart + 1, SeekOrigin.Begin); // +1 to skip '<'
        var hexSignature = BitConverter.ToString(signatureBytes).Replace("-", "");
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        _outputStream.Write(hexBytes, 0, hexBytes.Length);
    }

    public void Save(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Create);
        Begin(fs);
        
        /*
          For simple usage where Build wasn't called on pages,
          we might still have objects in the _ objects list if RegisterObject was called before Begin
          or in some other scenarios.
          But with the current logic, everything should be written via WriteObject
         */
        
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