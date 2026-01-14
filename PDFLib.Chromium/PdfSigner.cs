using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PDFLib.Models;

namespace PDFLib.Chromium;

/// <summary>
/// Provides functionality to sign PDF documents by injecting digital signature fields and metadata.
/// Supports incremental updates and multiple certificates.
/// </summary>
public class PdfSigner
{
    private readonly byte[] _pdfBytes;
    private readonly List<SignatureZone> _zones;
    private readonly List<PdfObject> _newObjects = new();
    private readonly Dictionary<int, long> _offsets = new();
    private readonly Dictionary<string, X509Certificate2> _certificates = new();
    private X509Certificate2? _defaultCertificate;
    private int _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfSigner"/> class.
    /// </summary>
    /// <param name="pdfBytes">The raw bytes of the PDF to sign.</param>
    /// <param name="zones">The list of signature zones detected in the document.</param>
    public PdfSigner(byte[] pdfBytes, List<SignatureZone> zones)
    {
        _pdfBytes = pdfBytes;
        _zones = zones;
    }

    /// <summary>
    /// Adds a certificate to be used for signing.
    /// </summary>
    /// <param name="certificate">The X.509 certificate to use.</param>
    /// <param name="zoneId">The ID of the signature zone to associate this certificate with. If null, it acts as a default.</param>
    public void AddCertificate(X509Certificate2 certificate, string? zoneId = null)
    {
        if (string.IsNullOrEmpty(zoneId))
        {
            _defaultCertificate = certificate;
        }
        else
        {
            _certificates[zoneId] = certificate;
        }
    }

    /// <summary>
    /// Performs the signing operation asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous signing operation. The task result contains the signed PDF bytes.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no certificates are provided.</exception>
    /// <exception cref="Exception">Thrown if the PDF structure is invalid or signing fails.</exception>
    public async Task<byte[]> SignAsync()
    {
        if (_zones.Count == 0) return _pdfBytes;

        var certToUse = _defaultCertificate;
        if (certToUse == null && _certificates.Count == 0)
            throw new InvalidOperationException("No certificates provided for signing.");

        var lastStartXref = FindLastStartXref();
        if (lastStartXref == -1) throw new Exception("Could not find startxref");

        var trailer = ParseTrailer(lastStartXref);
        var rootRef = (PdfReference)trailer.Get("/Root");
        _nextId = FindMaxId() + 1;

        using var ms = new MemoryStream();
        await ms.WriteAsync(_pdfBytes);

        var acroForm = new PdfDictionary
        {
            ObjectId = _nextId++
        };
        acroForm.Add("/Fields", new PdfArray());
        _newObjects.Add(acroForm);

        var sigDictToZoneId = new Dictionary<int, string>();

        // Add signature fields for each zone
        var sigFields = (PdfArray)acroForm.Get("/Fields");
        
        var pageRef = FindFirstPageRef(rootRef);

        foreach (var zone in _zones)
        {
            var (x, y, w, h, pageNum) = zone.ToPdfCoordinates();
            
            var sigDict = new PdfSignature(4096)
            {
                ObjectId = _nextId++
            };
            _newObjects.Add(sigDict);
            sigDictToZoneId[sigDict.ObjectId.Value] = zone.Id;

            var sigField = new PdfDictionary();
            sigField.ObjectId = _nextId++;
            sigField.Add("/Type", new PdfName("/Annot"));
            sigField.Add("/Subtype", new PdfName("/Widget"));
            sigField.Add("/FT", new PdfName("/Sig"));
            sigField.Add("/Rect", new PdfArray(new PdfNumber(x), new PdfNumber(y), new PdfNumber(x + w), new PdfNumber(y + h)));
            if (pageRef != null) sigField.Add("/P", pageRef);
            sigField.Add("/V", new PdfReference(sigDict.ObjectId.Value));
            sigField.Add("/T", new PdfString($"Signature-{zone.Id}"));
            sigField.Add("/F", new PdfNumber(4)); // Print flag
            
            _newObjects.Add(sigField);
            sigFields.Add(new PdfReference(sigField.ObjectId.Value));
        }

        // In an incremental update, we should provide the updated Root object
        var updatedRoot = new PdfDictionary();
        updatedRoot.ObjectId = rootRef.Id;
        updatedRoot.Add("/Type", new PdfName("/Catalog"));
        updatedRoot.Add("/AcroForm", new PdfReference(acroForm.ObjectId.Value));
        
        // Try to preserve /Pages from original root
        var rootText = GetObjectText(rootRef.Id);
        var pagesMatch = System.Text.RegularExpressions.Regex.Match(rootText, @"/Pages\s+(\d+)\s+\d+\s+R");
        if (pagesMatch.Success)
        {
            updatedRoot.Add("/Pages", new PdfReference(int.Parse(pagesMatch.Groups[1].Value)));
        }

        _newObjects.Add(updatedRoot);
        
        // Update Page 1 to include the new Annotations
        if (pageRef != null)
        {
            var updatedPage = new PdfDictionary();
            updatedPage.ObjectId = pageRef.Id;
            updatedPage.Add("/Type", new PdfName("/Page"));
            
            var annots = new PdfArray();
            foreach (var obj in _newObjects.OfType<PdfDictionary>().Where(o => o.GetOptional("/Subtype") is PdfName
                     {
                         Name: "/Widget"
                     }))
            {
                annots.Add(new PdfReference(obj.ObjectId!.Value));
            }
            updatedPage.Add("/Annots", annots);
            
            // Try to preserve other keys from original page object
            var pageText = GetObjectText(pageRef.Id);
            foreach (var key in new[] { "/Parent", "/Resources", "/MediaBox", "/Contents" })
            {
                var match = System.Text.RegularExpressions.Regex.Match(pageText, $@"{key}\s+([^\n\r]+)");
                if (!match.Success) continue;
                
                /*
                 * TODO: This is a bit hacky as we're pasting raw text, but PdfObject.WriteTo takes bytes
                 * Don't have a good Raw PdfObject
                 * Shall use Regex to find Ids if they are references
                 */
                var refMatch = System.Text.RegularExpressions.Regex.Match(match.Groups[1].Value, @"(\d+)\s+\d+\s+R");
                if (refMatch.Success)
                    updatedPage.Add(key, new PdfReference(int.Parse(refMatch.Groups[1].Value)));
                else if (match.Groups[1].Value.Contains('['))
                    updatedPage.Add(key, new PdfArray());
            }

            _newObjects.Add(updatedPage);
        }

        // Write new objects
        var writer = new BinaryWriter(ms);
        ms.Seek(0, SeekOrigin.End);
        
        foreach (var obj in _newObjects)
        {
            _offsets[obj.ObjectId!.Value] = ms.Position;
            writer.Write(Encoding.ASCII.GetBytes($"{obj.ObjectId} {obj.Generation} obj\n"));
            obj.WriteTo(writer);
            writer.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }

        var xrefOffset = ms.Position;
        writer.Write(Encoding.ASCII.GetBytes("xref\n"));
        
        var sortedIds = _offsets.Keys.OrderBy(k => k).ToList();
        // This is a simplified xref for incremental update
        foreach (var id in sortedIds)
        {
            writer.Write(Encoding.ASCII.GetBytes($"{id} 1\n"));
            writer.Write(Encoding.ASCII.GetBytes($"{_offsets[id]:D10} 00000 n \n"));
        }

        writer.Write("trailer\n"u8.ToArray());
        var newTrailer = new PdfDictionary();
        newTrailer.Add("/Size", new PdfNumber(_nextId));
        newTrailer.Add("/Root", rootRef);
        newTrailer.Add("/Prev", new PdfNumber(lastStartXref));
        
        writer.Write(newTrailer.GetBytes());
        writer.Write(Encoding.ASCII.GetBytes($"\nstartxref\n{xrefOffset}\n%%EOF"));
        writer.Flush();

        var signedBytes = ms.ToArray();
        
         // The final step is to actually sign the PDf with the certificate
        foreach (var obj in _newObjects.OfType<PdfSignature>())
        {
            var zoneId = sigDictToZoneId[obj.ObjectId!.Value];
            if (_certificates.TryGetValue(zoneId, out var cert))
            {
                SignDocument(signedBytes, obj, cert);
            }
            else if (_defaultCertificate != null)
            {
                SignDocument(signedBytes, obj, _defaultCertificate);
            }
        }

        return signedBytes;
    }

    private void SignDocument(byte[] pdfBytes, PdfSignature signature, X509Certificate2 certificate)
    {
        var contentsStart = _offsets[signature.ObjectId!.Value] + GetContentsRelativeOffset(signature);
        var contentsLength = 4096;

        var part1Length = (int)contentsStart;
        var part2Start = (int)(contentsStart + contentsLength);
        var part2Length = (int)(pdfBytes.Length - part2Start);

        var byteRange = $"[0 {part1Length} {part2Start} {part2Length}]";
        var byteRangeOffset = _offsets[signature.ObjectId!.Value] + GetByteRangeRelativeOffset(signature);

        var byteRangeBytes = Encoding.ASCII.GetBytes(byteRange.PadRight(64));
        Array.Copy(byteRangeBytes, 0, pdfBytes, byteRangeOffset, byteRangeBytes.Length);

        byte[] hash;
        using (var sha = SHA256.Create())
        {
            sha.TransformBlock(pdfBytes, 0, part1Length, null, 0);
            sha.TransformFinalBlock(pdfBytes, part2Start, part2Length);
            hash = sha.Hash!;
        }

        var contentInfo = new ContentInfo(hash);
        var signedCms = new SignedCms(contentInfo, true);
        var cmsSigner = new CmsSigner(certificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") // SHA256
        };

        signedCms.ComputeSignature(cmsSigner);
        var signatureBytes = signedCms.Encode();

        var hexSignature = BitConverter.ToString(signatureBytes).Replace("-", "");
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        
        Array.Copy(hexBytes, 0, pdfBytes, contentsStart + 1, hexBytes.Length);
    }

    private long GetContentsRelativeOffset(PdfSignature sig)
    {
        // Estimate based on PdfSignature.WriteTo
        // << /Type /Sig  /Filter /Adobe.PPKLite  /SubFilter /adbe.pkcs7.detached  /M (D:...)  /Contents 
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        sig.WriteTo(writer);
        return sig.ContentsOffset;
    }

    private long GetByteRangeRelativeOffset(PdfSignature sig)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        sig.WriteTo(writer);
        return sig.ByteRangeOffset;
    }

    private long FindLastStartXref()
    {
        var text = Encoding.ASCII.GetString(_pdfBytes);
        var index = text.LastIndexOf("startxref");
        if (index == -1) return -1;
        
        var nextLine = text.Substring(index + 9).TrimStart();
        var endOfNumber = 0;
        while (endOfNumber < nextLine.Length && char.IsDigit(nextLine[endOfNumber])) endOfNumber++;
        
        if (long.TryParse(nextLine.Substring(0, endOfNumber), out var offset)) return offset;
        return -1;
    }

    private PdfDictionary ParseTrailer(long startxref)
    {
        // Very basic parser: look for trailer << ... >>
        var text = Encoding.ASCII.GetString(_pdfBytes, (int)startxref, _pdfBytes.Length - (int)startxref);
        var trailerIndex = text.IndexOf("trailer");
        if (trailerIndex == -1) throw new Exception("Trailer not found");
        
        var startDict = text.IndexOf("<<", trailerIndex);
        var endDict = text.IndexOf(">>", startDict);
        var dictText = text.Substring(startDict, endDict - startDict + 2);
        
        var dict = new PdfDictionary();
        // Extract Root
        var rootMatch = System.Text.RegularExpressions.Regex.Match(dictText, @"/Root\s+(\d+)\s+(\d+)\s+R");
        if (rootMatch.Success)
        {
            dict.Add("/Root", new PdfReference(int.Parse(rootMatch.Groups[1].Value)));
        }
        
        return dict;
    }

    private PdfReference? FindFirstPageRef(PdfReference rootRef)
    {
        // Follow Root -> Pages -> Kids[0]
        var rootText = GetObjectText(rootRef.Id);
        var pagesMatch = System.Text.RegularExpressions.Regex.Match(rootText, @"/Pages\s+(\d+)\s+\d+\s+R");
        if (!pagesMatch.Success) return null;

        var pagesId = int.Parse(pagesMatch.Groups[1].Value);
        var pagesText = GetObjectText(pagesId);
        
        // Find first kid in Kids array
        var kidsMatch = System.Text.RegularExpressions.Regex.Match(pagesText, @"/Kids\s*\[\s*(\d+)\s+\d+\s+R");
        return kidsMatch.Success ? new PdfReference(int.Parse(kidsMatch.Groups[1].Value)) : null;
    }

    private string GetObjectText(int id)
    {
        var text = Encoding.ASCII.GetString(_pdfBytes);
        var match = System.Text.RegularExpressions.Regex.Match(text, $@"{id}\s+\d+\s+obj\s*(.+?)\s*endobj", System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success ? match.Value : "";
    }

    private int FindMaxId()
    {
        var text = Encoding.ASCII.GetString(_pdfBytes);
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(\d+)\s+\d+\s+obj");
        var maxId = 0;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out var id) && id > maxId) maxId = id;
        }
        return maxId;
    }
}
