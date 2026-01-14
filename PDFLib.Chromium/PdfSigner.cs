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

        // Map sigDict ObjectId to zone ID for later signing
        var sigDictToZoneId = new Dictionary<int, string>();

        // Add signature fields for each zone
        var sigFields = (PdfArray)acroForm.Get("/Fields");
        
        var firstPageRef = FindPageRef(rootRef, 1);

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
            
            var targetPageRef = FindPageRef(rootRef, pageNum) ?? firstPageRef;
            if (targetPageRef != null) sigField.Add("/P", targetPageRef);
            
            sigField.Add("/V", new PdfReference(sigDict.ObjectId.Value));
            sigField.Add("/T", new PdfString(zone.Id));
            sigField.Add("/F", new PdfNumber(4)); // Print flag
            
            _newObjects.Add(sigField);
            sigFields.Add(new PdfReference(sigField.ObjectId.Value));
        }

        // In an incremental update, we should provide the updated Root object
        var updatedRoot = new PdfDictionary();
        updatedRoot.ObjectId = rootRef.Id;
        
        // Try to preserve all keys from original root
        var rootText = GetObjectText(rootRef.Id);
        
        // Match all keys in the dictionary: /Key value
        // Refined regex to better handle nested dictionaries and arrays
        var keyMatches = System.Text.RegularExpressions.Regex.Matches(rootText, @"(/[^/ \(\)\[\]<>]+)\s+([^\n\r/<>\[\]]+|\[(?:[^\[\]]*|\[[^\[\]]*\])*\]|<<(?:[^<>]*|<<[^<>]*>>)*>>)");
        foreach (System.Text.RegularExpressions.Match match in keyMatches)
        {
            var key = match.Groups[1].Value;
            var val = match.Groups[2].Value.Trim();
            if (key == "/AcroForm") continue; // We'll add our own
            
            // If it's a simple reference, use PdfReference
            var refMatch = System.Text.RegularExpressions.Regex.Match(val, @"^(\d+)\s+\d+\s+R$");
            if (refMatch.Success)
            {
                updatedRoot.Add(key, new PdfReference(int.Parse(refMatch.Groups[1].Value)));
            }
            else
            {
                // For complex values, ensure they don't break the dictionary syntax
                updatedRoot.Add(key, new PdfRawObject(val));
            }
        }
        
        updatedRoot.Add("/AcroForm", new PdfReference(acroForm.ObjectId.Value));
        _newObjects.Add(updatedRoot);
        
        // Update Pages to include the new Annotations
        // Group signature zones by page number
        var zonesByPage = _zones.GroupBy(z => z.ToPdfCoordinates().pageNumber);
        
        foreach (var pageGroup in zonesByPage)
        {
            var pageNum = pageGroup.Key;
            var pageRefForThisPage = FindPageRef(rootRef, pageNum);

            if (pageRefForThisPage != null)
            {
                var updatedPage = new PdfDictionary();
                updatedPage.ObjectId = pageRefForThisPage.Id;

                var annots = new PdfArray();
                
                // Try to find existing annotations for this page to preserve them
                var pageText = GetObjectText(pageRefForThisPage.Id);
                
                // Preserve all keys from original page object
                var pageKeys = System.Text.RegularExpressions.Regex.Matches(pageText, @"(/[^/ \(\)\[\]<>]+)\s+([^\n\r/<>\[\]]+|\[(?:[^\[\]]*|\[[^\[\]]*\])*\]|<<(?:[^<>]*|<<[^<>]*>>)*>>)");
                foreach (System.Text.RegularExpressions.Match match in pageKeys)
                {
                    var key = match.Groups[1].Value;
                    var val = match.Groups[2].Value.Trim();
                    if (key == "/Annots")
                    {
                         // We'll handle /Annots separately
                         var refMatches = System.Text.RegularExpressions.Regex.Matches(val, @"(\d+)\s+\d+\s+R");
                         foreach (System.Text.RegularExpressions.Match m in refMatches)
                         {
                             annots.Add(new PdfReference(int.Parse(m.Groups[1].Value)));
                         }
                         continue;
                    }
                    
                    // If it's a simple reference, use PdfReference
                    var refMatch = System.Text.RegularExpressions.Regex.Match(val, @"^(\d+)\s+\d+\s+R$");
                    if (refMatch.Success)
                    {
                        updatedPage.Add(key, new PdfReference(int.Parse(refMatch.Groups[1].Value)));
                    }
                    else
                    {
                        updatedPage.Add(key, new PdfRawObject(val));
                    }
                }

                // Add our new signature widgets for this specific page
                foreach (var zone in pageGroup)
                {
                    // Find the signature field associated with this zone ID
                    var sigField = _newObjects.OfType<PdfDictionary>()
                        .FirstOrDefault(o => o.GetOptional("/Subtype") is PdfName { Name: "/Widget" } && 
                                           o.GetOptional("/T") is PdfString s && Encoding.ASCII.GetString(s.GetBytes()).Contains(zone.Id));
                    
                    if (sigField != null)
                    {
                        annots.Add(new PdfReference(sigField.ObjectId!.Value));
                    }
                }
                
                updatedPage.Add("/Annots", annots);
                _newObjects.Add(updatedPage);
            }
        }

        // Write new objects
        var writer = new BinaryWriter(ms);
        ms.Seek(0, SeekOrigin.End);
        
        foreach (var obj in _newObjects)
        {
            _offsets[obj.ObjectId!.Value] = ms.Position;
            writer.Write(Encoding.ASCII.GetBytes($"\n{obj.ObjectId} {obj.Generation} obj\n"));
            obj.WriteTo(writer);
            writer.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }

        var xrefOffset = ms.Position;
        // The startxref must point to the 'x' in 'xref'
        // If we added a leading newline, we need to adjust the offset
        writer.Write(Encoding.ASCII.GetBytes("\nxref\n"));
        xrefOffset++; // Skip the '\n' if we added one 
        
        var sortedIds = _offsets.Keys.OrderBy(k => k).ToList();
        // Standard xref: startId count
        if (sortedIds.Count > 0)
        {
            var currentRangeStart = sortedIds[0];
            var currentRangeCount = 1;
            
            var ranges = new List<(int start, int count)>();
            for (var i = 1; i < sortedIds.Count; i++)
            {
                if (sortedIds[i] == currentRangeStart + currentRangeCount)
                {
                    currentRangeCount++;
                }
                else
                {
                    ranges.Add((currentRangeStart, currentRangeCount));
                    currentRangeStart = sortedIds[i];
                    currentRangeCount = 1;
                }
            }
            ranges.Add((currentRangeStart, currentRangeCount));

            foreach (var range in ranges)
            {
                writer.Write(Encoding.ASCII.GetBytes($"{range.start} {range.count}\n"));
                for (var i = 0; i < range.count; i++)
                {
                    var id = range.start + i;
                    writer.Write(Encoding.ASCII.GetBytes($"{_offsets[id]:D10} 00000 n \n"));
                }
            }
        }
        else
        {
             // Fallback if somehow no new objects
             writer.Write(Encoding.ASCII.GetBytes("0 1\n0000000000 65535 f \n"));
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
        // Search backwards for startxref
        var marker = Encoding.ASCII.GetBytes("startxref");
        for (var i = _pdfBytes.Length - marker.Length; i >= 0; i--)
        {
            var match = true;
            for (var j = 0; j < marker.Length; j++)
            {
                if (_pdfBytes[i + j] != marker[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                var text = Encoding.ASCII.GetString(_pdfBytes, i + 9, Math.Min(100, _pdfBytes.Length - (i + 9)));
                var nextLine = text.TrimStart();
                var endOfNumber = 0;
                while (endOfNumber < nextLine.Length && char.IsDigit(nextLine[endOfNumber])) endOfNumber++;
                
                if (long.TryParse(nextLine.Substring(0, endOfNumber), out var offset)) return offset;
            }
        }
        return -1;
    }

    private PdfDictionary ParseTrailer(long startxref)
    {
        // Find trailer keyword
        var trailerMarker = Encoding.ASCII.GetBytes("trailer");
        var trailerIndex = -1;
        for (var i = (int)startxref; i <= _pdfBytes.Length - trailerMarker.Length; i++)
        {
            var match = true;
            for (var j = 0; j < trailerMarker.Length; j++)
            {
                if (_pdfBytes[i + j] != trailerMarker[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                trailerIndex = i;
                break;
            }
        }

        if (trailerIndex == -1) throw new Exception("Trailer not found");
        
        var text = Encoding.ASCII.GetString(_pdfBytes, trailerIndex, Math.Min(2048, _pdfBytes.Length - trailerIndex));
        
        var dict = new PdfDictionary();
        // Extract Root
        var rootMatch = System.Text.RegularExpressions.Regex.Match(text, @"/Root\s+(\d+)\s+(\d+)\s+R");
        if (rootMatch.Success)
        {
            dict.Add("/Root", new PdfReference(int.Parse(rootMatch.Groups[1].Value)));
        }
        
        return dict;
    }

    private PdfReference? FindPageRef(PdfReference rootRef, int pageNumber)
    {
        // Follow Root -> Pages -> Kids
        var rootText = GetObjectText(rootRef.Id);
        var pagesMatch = System.Text.RegularExpressions.Regex.Match(rootText, @"/Pages\s+(\d+)\s+\d+\s+R");
        if (!pagesMatch.Success) return null;

        var pagesId = int.Parse(pagesMatch.Groups[1].Value);
        var pagesText = GetObjectText(pagesId);
        
        // Match all kids in Kids array
        var kidsMatch = System.Text.RegularExpressions.Regex.Match(pagesText, @"/Kids\s*\[([^\]]*)\]");
        if (!kidsMatch.Success) return null;
        var kidsRefs = kidsMatch.Groups[1].Value;
        var refMatches = System.Text.RegularExpressions.Regex.Matches(kidsRefs, @"(\d+)\s+\d+\s+R");
            
        if (pageNumber > 0 && pageNumber <= refMatches.Count)
        {
            return new PdfReference(int.Parse(refMatches[pageNumber - 1].Groups[1].Value));
        }

        return null;
    }

    private string GetObjectText(int id)
    {
        // Search in binary for "{id} 0 obj" - more robust pattern
        var marker = Encoding.ASCII.GetBytes($"\n{id} 0 obj");
        var startIndex = -1;
        
        // Try with newline first
        startIndex = FindInBinary(_pdfBytes, marker);
        
        // If not found, try without leading newline but ensuring it's at start of line or preceded by whitespace
        if (startIndex == -1)
        {
            var marker2 = Encoding.ASCII.GetBytes($"{id} 0 obj");
            startIndex = FindInBinary(_pdfBytes, marker2);
        }
        
        if (startIndex == -1) return "";
        
        // Ensure we skip the leading newline if we used marker
        if (_pdfBytes[startIndex] == '\n') startIndex++;
        else if (_pdfBytes[startIndex] == '\r') startIndex++;

        // Find endobj
        var endMarker = Encoding.ASCII.GetBytes("endobj");
        var endIndex = FindInBinary(_pdfBytes, endMarker, startIndex);
        
        if (endIndex == -1) return "";
        
        return Encoding.ASCII.GetString(_pdfBytes, startIndex, endIndex - startIndex + 6);
    }

    private int FindInBinary(byte[] haystack, byte[] needle, int startSearch = 0)
    {
        for (var i = startSearch; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
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
        
        // Also check startxref trailer
        var lastStartXref = FindLastStartXref();
        if (lastStartXref != -1)
        {
             // Look for trailer after the last xref
             var trailerIndex = FindInBinary(_pdfBytes, Encoding.ASCII.GetBytes("trailer"), (int)lastStartXref);
             if (trailerIndex != -1)
             {
                 var trailerText = Encoding.ASCII.GetString(_pdfBytes, trailerIndex, Math.Min(1024, _pdfBytes.Length - trailerIndex));
                 var sizeMatch = System.Text.RegularExpressions.Regex.Match(trailerText, @"/Size\s+(\d+)");
                 if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var size))
                 {
                     if (size - 1 > maxId) maxId = size - 1;
                 }
             }
        }
        
        return maxId;
    }
}
