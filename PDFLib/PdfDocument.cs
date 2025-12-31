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
    private readonly PdfArray _kids = new();
    private readonly Dictionary<int, long> _offsets = new();
    private Stream? _outputStream;
    private BinaryWriter? _writer;
    private bool _isStreaming;

    public PdfDocument()
    {
    }

    private readonly Dictionary<string, (X509Certificate2 Cert, PdfSignature Sig, PdfFormXObject? Ap, PdfArray? Rect)> _signatures = new();
    private readonly Dictionary<string, PdfDictionary> _signatureFields = new();
    private readonly List<string> _pendingSignatureNames = new();

    public void AddSignature(X509Certificate2 certificate, int? x = null, int? y = null, int? width = null, int? height = null)
    {
        AddSignature("default", certificate, x, y, width, height);
    }

    public void AddSignature(string name, X509Certificate2 certificate, int? x = null, int? y = null, int? width = null, int? height = null)
    {
        var sig = new PdfSignature();
        _signatures[name] = (certificate, sig, null, null);
        
        if (x.HasValue && y.HasValue && width.HasValue && height.HasValue)
        {
            SetSignatureAppearance(name, x.Value, y.Value, width.Value, height.Value);
        }
    }

    public void SetSignatureAppearance(int x, int y, int width, int height)
    {
        SetSignatureAppearance("default", x, y, width, height);
    }

    public void SetSignatureAppearance(string name, int x, int y, int width, int height)
    {
        if (!_signatures.TryGetValue(name, out var data)) return;

        var sigRect = new PdfArray();
        sigRect.Add(new PdfNumber(x));
        sigRect.Add(new PdfNumber(y));
        sigRect.Add(new PdfNumber(x + width));
        sigRect.Add(new PdfNumber(y + height));

        // Create appearance
        var signatureAppearance = new PdfFormXObject(width, height);
        signatureAppearance.AddFont("F1", "Helvetica", this);
        
        var certName = data.Cert.GetNameInfo(X509NameType.SimpleName, false) ?? data.Cert.Subject;
        signatureAppearance.DrawText("F1", 10, 2, height - 12, $"Digitally signed by: {certName}");
        signatureAppearance.DrawText("F1", 8, 2, height - 24, $"Date: {DateTime.Now:yyyy.MM.dd HH:mm:ss zzz}");
        signatureAppearance.DrawLine(0, 0, width, 0);
        signatureAppearance.DrawLine(0, 0, 0, height);
        signatureAppearance.DrawLine(width, 0, width, height);
        signatureAppearance.DrawLine(0, height, width, height);
        signatureAppearance.Build();

        _signatures[name] = (data.Cert, data.Sig, signatureAppearance, sigRect);

        // Update the field if it exists
        if (_signatureFields.TryGetValue(name, out var sigField))
        {
            sigField.Add("/Rect", sigRect);
            var ap = new PdfDictionary();
            ap.Add("/N", signatureAppearance);
            sigField.Add("/AP", ap);
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

        if (_signatures.Count <= 0) return;
        
        var acroForm = new PdfDictionary();
        var fields = new PdfArray();
        acroForm.Add("/Fields", fields);
        acroForm.Add("/SigFlags", new PdfNumber(3)); // 1 (SignaturesExist) + 2 (AppendOnly)
        _catalog.Add("/AcroForm", acroForm);

        int i = 1;
        foreach (var kvp in _signatures)
        {
            var name = kvp.Key;
            var data = kvp.Value;

            var sigField = new PdfDictionary();
            sigField.Add("/Type", new PdfName("/Annot"));
            sigField.Add("/Subtype", new PdfName("/Widget"));
            sigField.Add("/FT", new PdfName("/Sig"));
            sigField.Add("/T", new PdfString($"Signature{i++}"));
            sigField.Add("/V", data.Sig);
            sigField.Add("/Rect", data.Rect ?? new PdfArray());
            sigField.Add("/F", new PdfNumber(4)); // Print flag

            if (data.Ap != null)
            {
                var ap = new PdfDictionary();
                ap.Add("/N", data.Ap);
                sigField.Add("/AP", ap);
            }

            fields.Add(sigField);
            _signatureFields[name] = sigField;
            _pendingSignatureNames.Add(name);
        }
    }

    private int AssignId() => _nextId++;

    public PdfPage AddPage()
    {
        if (!_isStreaming) throw new InvalidOperationException("Call Begin() first");
        
        var pagesId = _pages.ObjectId ?? throw new InvalidOperationException("Pages object ID not set");
        var page = new PdfPage(this, AssignId(), pagesId);
        _kids.Add(new PdfReference(page.PageDict.ObjectId!.Value));

        // We'll need a way to add a specific signature field to a page
        return page;
    }

    public void AddSignatureToPage(PdfPage page, string signatureName)
    {
        if (_signatureFields.TryGetValue(signatureName, out var sigField) && _pendingSignatureNames.Contains(signatureName))
        {
            var data = _signatures[signatureName];
            
            // Register related objects if not yet done
            if (data.Sig.ObjectId == null) RegisterObject(data.Sig);
            if (data.Ap != null && data.Ap.ObjectId == null) RegisterObject(data.Ap);
            
            // Register the field itself
            RegisterObject(sigField);

            PdfArray annots;
            var existing = page.PageDict.GetOptional("/Annots");
            if (existing is PdfArray arr)
            {
                annots = arr;
            }
            else
            {
                annots = new PdfArray();
                page.PageDict.Add("/Annots", annots);
            }
            annots.Add(sigField);
            _pendingSignatureNames.Remove(signatureName);
        }
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

        // Register any remaining (invisible) signature fields
        foreach (var name in _pendingSignatureNames.ToList())
        {
            var data = _signatures[name];
            var sigField = _signatureFields[name];
            
            if (data.Sig.ObjectId == null) RegisterObject(data.Sig);
            if (data.Ap != null && data.Ap.ObjectId == null) RegisterObject(data.Ap);
            RegisterObject(sigField);
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
            _writer.Write(_offsets.TryGetValue(i, out var offset)
                ? Encoding.ASCII.GetBytes($"{offset:D10} 00000 n \n")
                : Encoding.ASCII.GetBytes("0000000000 00000 f \n"));
        }

        _writer.Write(Encoding.ASCII.GetBytes("trailer\n"));
        var trailer = new PdfDictionary();
        trailer.Add("/Size", new PdfNumber(maxId + 1));
        trailer.Add("/Root", new PdfReference(_catalog.ObjectId!.Value));

        _writer.Write(trailer.GetBytes());
        _writer.Write(Encoding.ASCII.GetBytes($"\nstartxref\n{xrefStart}\n%%EOF"));
        
        _writer.Flush();

        foreach (var sigData in _signatures.Values)
        {
            SignDocument(sigData.Sig, sigData.Cert);
        }

        _isStreaming = false;
    }

    private void SignDocument(PdfSignature signature, X509Certificate2 certificate)
    {
        if (_outputStream == null) return;

        var fileLength = _outputStream.Position;
        
        var contentsStart = signature.ContentsOffset;
        var contentsLength = 4096; 

        var part1Length = (int)contentsStart;
        var part2Start = (int)(contentsStart + contentsLength);
        var part2Length = (int)(fileLength - part2Start);

        var byteRange = $"[0 {part1Length} {part2Start} {part2Length}]";
        
        _outputStream.Seek(signature.ByteRangeOffset, SeekOrigin.Begin);
        var byteRangeBytes = Encoding.ASCII.GetBytes(byteRange.PadRight(64));
        _outputStream.Write(byteRangeBytes, 0, byteRangeBytes.Length);

        byte[] hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            _outputStream.Seek(0, SeekOrigin.Begin);
            
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
        var cmsSigner = new System.Security.Cryptography.Pkcs.CmsSigner(certificate)
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