using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PDFLib.Models;

namespace PDFLib.Chromium;

/// <summary>
/// Describes a signature field found in a PDF.
/// </summary>
/// <param name="Name">The field name. For PDFs made by this library it is the id of the signature zone.</param>
/// <param name="IsSigned">Whether the field already carries a signature.</param>
public sealed record PdfSignatureField(string Name, bool IsSigned);

/// <summary>
/// Signs PDF documents. Creates signature fields for the zones found at render time and applies
/// certificate signatures, now or later, to any number of fields.
/// </summary>
/// <remarks>
/// <para>
/// Every signature is written as its own incremental update appended to the file. A signature covers the
/// whole file as it exists when it is made, so appending is the only way a later signature can be added
/// without invalidating the earlier ones.
/// </para>
/// <para>
/// Signing later works on the output of an earlier call: load the bytes, add a certificate for an
/// unsigned field and call <see cref="SignAsync"/> again. No zones are needed for that.
/// </para>
/// </remarks>
public class PdfSigner
{
    private readonly byte[] _pdfBytes;
    private readonly List<SignatureZone> _zones;
    private readonly Dictionary<string, X509Certificate2> _certificates = new();
    private readonly List<string> _certificateOrder = new();
    private X509Certificate2? _defaultCertificate;
    private int _reservedSignatureSize = 4096;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfSigner"/> class for a PDF whose signature fields
    /// already exist, e.g. to add a signature to a document that was rendered (and possibly signed) earlier.
    /// </summary>
    /// <param name="pdfBytes">The raw bytes of the PDF to sign.</param>
    public PdfSigner(byte[] pdfBytes) : this(pdfBytes, new List<SignatureZone>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfSigner"/> class.
    /// </summary>
    /// <param name="pdfBytes">The raw bytes of the PDF to sign.</param>
    /// <param name="zones">
    /// The signature zones detected in the document. A signature field is created for every zone that
    /// doesn't have one yet, whether or not a certificate is supplied for it now.
    /// </param>
    public PdfSigner(byte[] pdfBytes, List<SignatureZone> zones)
    {
        _pdfBytes = pdfBytes ?? throw new ArgumentNullException(nameof(pdfBytes));
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
    }

    /// <summary>
    /// Gets or sets how many characters are reserved in the file for each signature. A signature needs two
    /// characters per byte, so the default of 4096 holds about 2 KB. Raise it for long certificate chains.
    /// </summary>
    public int ReservedSignatureSize
    {
        get => _reservedSignatureSize;
        set
        {
            if (value < 1024 || value % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Must be an even number of at least 1024.");
            _reservedSignatureSize = value;
        }
    }

    /// <summary>
    /// Adds a certificate to be used for signing. Signatures are applied in the order certificates are added.
    /// </summary>
    /// <param name="certificate">The X.509 certificate to use. It must have a private key.</param>
    /// <param name="zoneId">
    /// The signature zone (field name) to sign with this certificate. If null, the certificate is the default:
    /// it signs every zone passed to the constructor that has no certificate of its own or, when no zones were
    /// passed, every unsigned signature field in the document.
    /// </param>
    public void AddCertificate(X509Certificate2 certificate, string? zoneId = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (string.IsNullOrEmpty(zoneId))
        {
            _defaultCertificate = certificate;
            return;
        }

        if (!_certificates.ContainsKey(zoneId)) _certificateOrder.Add(zoneId);
        _certificates[zoneId] = certificate;
    }

    /// <summary>
    /// Lists the signature fields that already exist in the PDF and whether each one is signed.
    /// Useful before signing later, to see what is left to sign.
    /// </summary>
    public IReadOnlyList<PdfSignatureField> GetSignatureFields()
    {
        var reader = PdfFileReader.Parse(_pdfBytes);
        return ReadFields(reader).Select(f => new PdfSignatureField(f.Name, f.IsSigned)).ToList();
    }

    /// <summary>
    /// Performs the signing operation asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous signing operation. The task result contains the signed PDF bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a certificate targets a field that doesn't exist or is already signed, or a signature
    /// doesn't fit in <see cref="ReservedSignatureSize"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">Thrown if the PDF structure is invalid.</exception>
    /// <exception cref="NotSupportedException">Thrown if the PDF uses cross-reference streams.</exception>
    public Task<byte[]> SignAsync()
    {
        return Task.FromResult(Sign());
    }

    private byte[] Sign()
    {
        var output = new MemoryStream(_pdfBytes.Length + 64 * 1024);
        output.Write(_pdfBytes);

        var reader = Read(output);
        var existing = ReadFields(reader);

        var missingZones = _zones
            .Where(z => existing.All(f => f.Name != z.Id))
            .DistinctBy(z => z.Id)
            .ToList();

        var targets = ResolveTargets(existing, missingZones);

        /*
         * First update: create every missing field (all zones, signed now or not), so later signers only
         * ever have to append a signature. If the first signature is for one of the new fields it goes into
         * the same update, which saves a revision in the common single-signature case.
         */
        if (missingZones.Count > 0)
        {
            (string Name, X509Certificate2 Certificate)? folded = null;
            if (targets.Count > 0 && missingZones.Any(z => z.Id == targets[0].Name))
            {
                folded = targets[0];
                targets.RemoveAt(0);
            }

            var signature = WriteFieldsUpdate(output, reader, missingZones, folded?.Name);
            if (signature != null) ApplySignature(output, signature, folded!.Value.Certificate);
        }

        // Every further signature: its own update holding the signature and the field now pointing at it
        foreach (var (name, certificate) in targets)
        {
            reader = Read(output);
            var field = ReadFields(reader).First(f => f.Name == name);

            var signature = WriteSignatureUpdate(output, reader, field);
            ApplySignature(output, signature, certificate);
        }

        return output.ToArray();
    }

    private static PdfFileReader Read(MemoryStream output)
    {
        return PdfFileReader.Parse(output.GetBuffer().AsMemory(0, (int)output.Length));
    }

    private List<(string Name, X509Certificate2 Certificate)> ResolveTargets(List<FieldInfo> existing,
        List<SignatureZone> missingZones)
    {
        var targets = new List<(string Name, X509Certificate2 Certificate)>();

        foreach (var name in _certificateOrder)
        {
            var field = existing.FirstOrDefault(f => f.Name == name);
            if (field == null && missingZones.All(z => z.Id != name))
            {
                var known = existing.Select(f => f.Name).Concat(missingZones.Select(z => z.Id)).ToList();
                throw new InvalidOperationException(
                    $"There is no signature field or zone named '{name}'. " +
                    (known.Count > 0 ? $"Available: {string.Join(", ", known)}." : "The document has no signature fields."));
            }

            if (field is { IsSigned: true })
                throw new InvalidOperationException($"Signature field '{name}' is already signed.");

            targets.Add((name, _certificates[name]));
        }

        if (_defaultCertificate != null)
        {
            var candidates = _zones.Count > 0
                ? _zones.Select(z => z.Id).Distinct()
                : existing.Select(f => f.Name);

            foreach (var name in candidates)
            {
                if (_certificates.ContainsKey(name)) continue;
                if (existing.Any(f => f.Name == name && f.IsSigned)) continue;
                targets.Add((name, _defaultCertificate));
            }
        }

        return targets;
    }

    // ------------------------------------------------------------------ updates

    /// <summary>
    /// Appends the update that adds signature fields (and their widgets) for <paramref name="zones"/>.
    /// </summary>
    /// <returns>The signature dictionary to fill in when <paramref name="signNow"/> names one of the zones.</returns>
    private PdfSignature? WriteFieldsUpdate(MemoryStream output, PdfFileReader reader, List<SignatureZone> zones,
        string? signNow)
    {
        var objects = new List<PdfObject>();
        var nextId = reader.Size;
        PdfSignature? signature = null;

        var newFieldRefs = new List<PdfObject>();
        var widgetsByPage = new Dictionary<int, List<PdfObject>>();
        var firstPage = reader.FindPage(1);

        foreach (var zone in zones)
        {
            var (x, y, w, h, pageNumber) = zone.ToPdfCoordinates();
            var pageId = reader.FindPage(pageNumber) ?? firstPage;

            var field = new PdfDictionary { ObjectId = nextId++ };
            field.Add("/Type", new PdfName("/Annot"));
            field.Add("/Subtype", new PdfName("/Widget"));
            field.Add("/FT", new PdfName("/Sig"));
            field.Add("/Rect", new PdfArray(new PdfNumber(x), new PdfNumber(y), new PdfNumber(x + w), new PdfNumber(y + h)));
            if (pageId != null) field.Add("/P", new PdfReference(pageId.Value));
            field.Add("/T", new PdfString(zone.Id));
            field.Add("/F", new PdfNumber(4)); // Print flag

            if (zone.Id == signNow)
            {
                signature = new PdfSignature(_reservedSignatureSize) { ObjectId = nextId++ };
                objects.Add(signature);
                field.Add("/V", new PdfReference(signature.ObjectId.Value));
            }

            // The visible part is already drawn on the page by Chromium; the widget itself stays transparent
            var appearance = CreateAppearance(w, h);
            appearance.ObjectId = nextId++;
            objects.Add(appearance);

            var apDict = new PdfDictionary();
            apDict.Add("/N", new PdfReference(appearance.ObjectId.Value));
            field.Add("/AP", apDict);

            objects.Add(field);
            newFieldRefs.Add(new PdfReference(field.ObjectId.Value));

            if (pageId == null) continue;
            if (!widgetsByPage.TryGetValue(pageId.Value, out var widgets))
                widgetsByPage[pageId.Value] = widgets = new List<PdfObject>();
            widgets.Add(new PdfReference(field.ObjectId.Value));
        }

        // AcroForm: extend the existing one, or create it and hook it into the catalog
        var catalog = reader.GetDictionary(reader.RootId)
                      ?? throw new InvalidDataException("Not a valid PDF: the document catalog could not be read.");

        var acroFormId = 0;
        var hasAcroForm = catalog.TryGet("/AcroForm", out var acroFormToken);
        var acroFormIsIndirect = hasAcroForm && acroFormToken.TryGetReference(out acroFormId);
        var existingAcroForm = hasAcroForm ? reader.ResolveDictionary(acroFormToken) : null;

        var acroForm = BuildAcroForm(reader, existingAcroForm, newFieldRefs);
        acroForm.ObjectId = acroFormIsIndirect && existingAcroForm != null ? acroFormId : nextId++;
        objects.Add(acroForm);

        if (!acroFormIsIndirect || existingAcroForm == null)
        {
            var updatedCatalog = CopyDictionary(catalog, "/AcroForm");
            updatedCatalog.ObjectId = reader.RootId;
            updatedCatalog.Add("/AcroForm", new PdfReference(acroForm.ObjectId.Value));
            objects.Add(updatedCatalog);
        }

        // Pages: add the new widgets to /Annots, keeping whatever annotations are already there
        foreach (var (pageId, widgets) in widgetsByPage)
        {
            var page = reader.GetDictionary(pageId);
            if (page == null) continue;

            var annots = new PdfArray();
            if (page.TryGet("/Annots", out var annotsToken))
                foreach (var annot in reader.ResolveArray(annotsToken))
                    annots.Add(new PdfRawObject(annot.ToArray()));
            foreach (var widget in widgets) annots.Add(widget);

            var updatedPage = CopyDictionary(page, "/Annots");
            updatedPage.ObjectId = pageId;
            updatedPage.Add("/Annots", annots);
            objects.Add(updatedPage);
        }

        WriteUpdate(output, reader, objects, nextId);
        return signature;
    }

    /// <summary>
    /// Appends the update that signs an existing, unsigned field: a new signature dictionary plus the
    /// field re-written to point at it.
    /// </summary>
    private PdfSignature WriteSignatureUpdate(MemoryStream output, PdfFileReader reader, FieldInfo field)
    {
        var objects = new List<PdfObject>();
        var nextId = reader.Size;

        var signature = new PdfSignature(_reservedSignatureSize) { ObjectId = nextId++ };
        objects.Add(signature);

        var updatedField = CopyDictionary(field.Dictionary, "/V");
        updatedField.ObjectId = field.ObjectId;
        updatedField.Add("/V", new PdfReference(signature.ObjectId.Value));
        objects.Add(updatedField);

        // Readers only treat the form as signed when /SigFlags says so; fix it up if another tool left it out
        var catalog = reader.GetDictionary(reader.RootId);
        if (catalog != null && catalog.TryGet("/AcroForm", out var acroFormToken) &&
            acroFormToken.TryGetReference(out var acroFormId) &&
            reader.GetDictionary(acroFormId) is { } acroForm && !HasSigFlags(reader, acroForm))
        {
            var updatedAcroForm = BuildAcroForm(reader, acroForm, []);
            updatedAcroForm.ObjectId = acroFormId;
            objects.Add(updatedAcroForm);
        }

        WriteUpdate(output, reader, objects, nextId);
        return signature;
    }

    private static bool HasSigFlags(PdfFileReader reader, PdfDictionaryToken acroForm)
    {
        return acroForm.TryGet("/SigFlags", out var flags) && reader.Resolve(flags) is { } resolved &&
               resolved.TryGetNumber(out var value) && ((int)value & 3) == 3;
    }

    private static PdfDictionary BuildAcroForm(PdfFileReader reader, PdfDictionaryToken? existing, List<PdfObject> newFields)
    {
        var fields = new PdfArray();
        var acroForm = new PdfDictionary();

        if (existing != null)
        {
            foreach (var (key, value) in existing.Entries)
            {
                if (key is "/Fields" or "/SigFlags") continue;
                acroForm.Add(key, new PdfRawObject(value.ToArray()));
            }

            if (existing.TryGet("/Fields", out var fieldsToken))
                foreach (var field in reader.ResolveArray(fieldsToken))
                    fields.Add(new PdfRawObject(field.ToArray()));
        }

        foreach (var field in newFields) fields.Add(field);

        acroForm.Add("/Fields", fields);
        acroForm.Add("/SigFlags", new PdfNumber(3)); // SignaturesExist | AppendOnly
        return acroForm;
    }

    /// <summary>Copies a dictionary byte-for-byte, leaving out the keys that are about to be replaced.</summary>
    private static PdfDictionary CopyDictionary(PdfDictionaryToken source, params string[] except)
    {
        var copy = new PdfDictionary();
        foreach (var (key, value) in source.Entries)
        {
            if (Array.IndexOf(except, key) >= 0) continue;
            copy.Add(key, new PdfRawObject(value.ToArray()));
        }

        return copy;
    }

    /// <summary>
    /// Appends objects, their cross-reference section and a trailer chained to the previous one.
    /// </summary>
    private static void WriteUpdate(MemoryStream output, PdfFileReader reader, List<PdfObject> objects, int nextId)
    {
        output.Seek(0, SeekOrigin.End);
        var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        var offsets = new SortedDictionary<int, long>();

        foreach (var obj in objects)
        {
            writer.Write((byte)'\n');
            writer.Flush();
            // The xref entry must point at the object number itself, not at the newline before it
            offsets[obj.ObjectId!.Value] = output.Position;
            writer.Write(Encoding.ASCII.GetBytes($"{obj.ObjectId} {obj.Generation} obj\n"));
            // PdfSignature records where its /Contents and /ByteRange placeholders land in the stream
            obj.WriteTo(writer);
            writer.Write("\nendobj\n"u8);
        }

        writer.Write((byte)'\n');
        writer.Flush();
        var xrefOffset = output.Position;
        writer.Write("xref\n"u8);

        // Consecutive object numbers share a subsection: "first count"
        var ids = offsets.Keys.ToList();
        for (var i = 0; i < ids.Count;)
        {
            var count = 1;
            while (i + count < ids.Count && ids[i + count] == ids[i] + count) count++;

            writer.Write(Encoding.ASCII.GetBytes($"{ids[i]} {count}\n"));
            for (var n = 0; n < count; n++)
                writer.Write(Encoding.ASCII.GetBytes($"{offsets[ids[i + n]]:D10} 00000 n \n"));

            i += count;
        }

        // The new trailer repeats the previous one (root, info, id) and links back to it through /Prev
        var trailer = new PdfDictionary();
        trailer.Add("/Size", new PdfNumber(Math.Max(reader.Size, nextId)));
        foreach (var key in new[] { "/Root", "/Info", "/ID" })
            if (reader.Trailer.TryGet(key, out var value))
                trailer.Add(key, new PdfRawObject(value.ToArray()));
        trailer.Add("/Prev", new PdfNumber(reader.LastStartXref.ToString()));

        writer.Write("trailer\n"u8);
        writer.Write(trailer.GetBytes());
        writer.Write(Encoding.ASCII.GetBytes($"\nstartxref\n{xrefOffset}\n%%EOF\n"));
        writer.Flush();
    }

    // ------------------------------------------------------------------ signing

    /// <summary>
    /// Fills in /ByteRange and /Contents of a signature dictionary that was just written at the end of
    /// <paramref name="output"/>. The signature covers the entire file except the /Contents value itself.
    /// </summary>
    private void ApplySignature(MemoryStream output, PdfSignature signature, X509Certificate2 certificate)
    {
        var pdf = output.GetBuffer();
        var length = (int)output.Length;

        var contentsStart = (int)signature.ContentsOffset; // the '<'
        var contentsLength = _reservedSignatureSize; // '<' + hex digits + '>'

        var part1Length = contentsStart;
        var part2Start = contentsStart + contentsLength;
        var part2Length = length - part2Start;

        var byteRange = Encoding.ASCII.GetBytes($"[0 {part1Length} {part2Start} {part2Length}]".PadRight(64));
        Buffer.BlockCopy(byteRange, 0, pdf, (int)signature.ByteRangeOffset, byteRange.Length);

        /*
         * A detached CMS signature must be computed over the signed byte ranges themselves; SignedCms does
         * the hashing. Passing a pre-computed hash as the content made it sign the hash of the hash, which
         * every validator rejects ("signature is invalid / document has been altered").
         */
        var signedContent = new byte[part1Length + part2Length];
        Buffer.BlockCopy(pdf, 0, signedContent, 0, part1Length);
        Buffer.BlockCopy(pdf, part2Start, signedContent, part1Length, part2Length);

        var signedCms = new SignedCms(new ContentInfo(signedContent), true);
        var cmsSigner = new CmsSigner(certificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") // SHA256
        };

        signedCms.ComputeSignature(cmsSigner);
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(signedCms.Encode()));

        if (hex.Length > contentsLength - 2)
            throw new InvalidOperationException(
                $"The signature needs {hex.Length} characters but only {contentsLength - 2} are reserved. " +
                $"Increase {nameof(ReservedSignatureSize)}.");

        Buffer.BlockCopy(hex, 0, pdf, contentsStart + 1, hex.Length);
    }

    // ------------------------------------------------------------------ fields

    private sealed record FieldInfo(string Name, int ObjectId, bool IsSigned, PdfDictionaryToken Dictionary);

    /// <summary>Lists the signature fields reachable from the catalog's AcroForm.</summary>
    private static List<FieldInfo> ReadFields(PdfFileReader reader)
    {
        var result = new List<FieldInfo>();

        var catalog = reader.GetDictionary(reader.RootId);
        if (catalog == null || !catalog.TryGet("/AcroForm", out var acroFormToken)) return result;

        var acroForm = reader.ResolveDictionary(acroFormToken);
        if (acroForm == null || !acroForm.TryGet("/Fields", out var fieldsToken)) return result;

        foreach (var field in reader.ResolveArray(fieldsToken))
            CollectFields(reader, field, null, false, result, 0);

        return result;
    }

    private static void CollectFields(PdfFileReader reader, PdfToken token, string? parentName, bool parentIsSignature,
        List<FieldInfo> result, int depth)
    {
        if (depth > 16 || !token.TryGetReference(out var objectId)) return;

        var dictionary = reader.GetDictionary(objectId);
        if (dictionary == null) return;

        // Field names are hierarchical: a child's full name is "parent.child"
        var partialName = dictionary.TryGet("/T", out var nameToken) ? reader.Resolve(nameToken)?.AsText() : null;
        var name = partialName == null ? parentName : parentName == null ? partialName : $"{parentName}.{partialName}";

        var isSignature = parentIsSignature;
        if (dictionary.TryGet("/FT", out var typeToken))
            isSignature = reader.Resolve(typeToken)?.AsName() == "/Sig";

        // Kids that have a name of their own are fields; kids without one are just this field's widgets
        var hasChildFields = false;
        if (dictionary.TryGet("/Kids", out var kidsToken))
        {
            foreach (var kid in reader.ResolveArray(kidsToken))
            {
                if (!kid.TryGetReference(out var kidId) || reader.GetDictionary(kidId) is not { } kidDictionary) continue;
                if (!kidDictionary.ContainsKey("/T")) continue;

                hasChildFields = true;
                CollectFields(reader, kid, name, isSignature, result, depth + 1);
            }
        }

        if (hasChildFields || !isSignature || name == null) return;

        var isSigned = dictionary.TryGet("/V", out var valueToken) &&
                       reader.Resolve(valueToken) is { Kind: PdfTokenKind.Dictionary };
        result.Add(new FieldInfo(name, objectId, isSigned, dictionary));
    }

    /// <summary>
    /// Creates a transparent appearance stream for the signature.
    /// </summary>
    /// <param name="w">The width of the signature zone in PDF points.</param>
    /// <param name="h">The height of the signature zone in PDF points.</param>
    /// <returns>A <see cref="PdfStreamObject"/> representing the transparent appearance.</returns>
    /// <remarks>
    /// This method only requires dimensions because the appearance stream's bounding box (/BBox)
    /// is relative to itself (0,0). The absolute positioning on the page is handled by the
    /// /Rect entry in the signature field dictionary.
    /// </remarks>
    private static PdfStreamObject CreateAppearance(double w, double h)
    {
        var dict = new PdfDictionary();
        dict.Add("/Type", new PdfName("/XObject"));
        dict.Add("/Subtype", new PdfName("/Form"));
        dict.Add("/BBox", new PdfArray(new PdfNumber(0), new PdfNumber(0), new PdfNumber(w), new PdfNumber(h)));
        dict.Add("/Resources", new PdfDictionary());

        var content = "% Transparent appearance";
        dict.Add("/Length", new PdfNumber(content.Length));

        return new PdfStreamObject(dict, content);
    }
}
