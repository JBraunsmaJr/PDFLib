using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PDFLib;

namespace PDFLib.Chromium.Tests;

/// <summary>
/// Checks signatures the way a PDF reader does: the CMS blob in /Contents must verify against the
/// bytes covered by /ByteRange, and /ByteRange must cover everything up to the end of the revision the
/// signature was made in, except /Contents itself.
/// </summary>
public class SignatureValidationTests
{
    private static X509Certificate2 CreateCertificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
    }

    private static SignatureZone Zone(string id, int page = 1) =>
        // Zones are in CSS pixels from the top of the document; a page is 792pt = 1056px tall
        new() { Id = id, X = 100, Y = (page - 1) * 1056 + 200, Width = 240, Height = 80 };

    /// <summary>Builds a PDF with a classic xref table from object bodies (object numbers start at 1).</summary>
    private static byte[] BuildPdf(params string[] objects)
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {objects.Length + 1}\n/Root 1 0 R\n/ID [<0A0B> <0C0D>]>>\nstartxref\n{xref}\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>A one-page PDF laid out the way Chromium writes them.</summary>
    private static byte[] MinimalPdf() => BuildPdf(
        "<</Type /Catalog\n/Pages 2 0 R\n/Lang (en-US \\(test\\))>>",
        "<</Type /Pages\n/Count 1\n/Kids [3 0 R]>>",
        "<</Type /Page\n/Resources <</ProcSet [/PDF /Text]>>\n/MediaBox [0 0 612 792]\n/Contents 4 0 R\n/Tabs /S\n/Parent 2 0 R>>",
        "<</Length 0>>\nstream\n\nendstream");

    /// <summary>
    /// Validates every signature in the file and returns the signers' subjects in file order.
    /// </summary>
    private static List<string> ValidateSignatures(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var signers = new List<string>();

        // Offsets where a revision can end: right after its %%EOF, or after the line break that follows it
        var revisionEnds = Regex.Matches(text, "%%EOF\r?\n?")
            .SelectMany(m => new[] { m.Index + 5, m.Index + m.Length })
            .ToHashSet();

        var ranges = Regex.Matches(text, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        for (var n = 0; n < ranges.Count; n++)
        {
            var r = ranges[n].Groups.Values.Skip(1).Select(g => int.Parse(g.Value)).ToArray();
            var end = r[2] + r[3];

            // Covers the file from the start to the end of its own revision, minus the /Contents value
            Assert.Equal(0, r[0]);
            Assert.Contains(end, revisionEnds);
            if (n == ranges.Count - 1) Assert.Equal(pdf.Length, end);
            Assert.Equal((byte)'<', pdf[r[1]]);
            Assert.Equal((byte)'>', pdf[r[2] - 1]);
            Assert.Equal("/Contents ", text.Substring(r[1] - 10, 10));

            var hex = text.Substring(r[1] + 1, r[2] - r[1] - 2);
            Assert.Matches("^[0-9A-Fa-f]+$", hex); // padded with '0', no stray bytes
            var der = Convert.FromHexString(hex);
            Assert.Equal(0x30, der[0]);
            Assert.Equal(0x82, der[1]);
            var derLength = 4 + ((der[2] << 8) | der[3]);

            var signedContent = new byte[r[1] + r[3]];
            Buffer.BlockCopy(pdf, 0, signedContent, 0, r[1]);
            Buffer.BlockCopy(pdf, r[2], signedContent, r[1], r[3]);

            var cms = new SignedCms(new ContentInfo(signedContent), detached: true);
            cms.Decode(der.AsSpan(0, derLength).ToArray());
            cms.CheckSignature(verifySignatureOnly: true); // throws if the document digest doesn't match

            signers.Add(cms.SignerInfos[0].Certificate!.Subject);
        }

        return signers;
    }

    [Fact]
    public async Task SingleSignature_Validates()
    {
        using var cert = CreateCertificate("Alice");
        var signer = new PdfSigner(MinimalPdf(), [Zone("signature-area-1")]);
        signer.AddCertificate(cert, "signature-area-1");

        var signed = await signer.SignAsync();

        Assert.Equal(["CN=Alice"], ValidateSignatures(signed));
    }

    [Fact]
    public async Task TwoCertificatesAtOnce_BothValidate_InTheOrderAdded()
    {
        using var alice = CreateCertificate("Alice");
        using var bob = CreateCertificate("Bob");
        var signer = new PdfSigner(MinimalPdf(), [Zone("signature-area-1"), Zone("signature-area-2")]);
        signer.AddCertificate(bob, "signature-area-2");
        signer.AddCertificate(alice, "signature-area-1");

        var signed = await signer.SignAsync();

        Assert.Equal(["CN=Bob", "CN=Alice"], ValidateSignatures(signed));
    }

    [Fact]
    public async Task DefaultCertificate_SignsEveryZoneWithoutItsOwn()
    {
        using var alice = CreateCertificate("Alice");
        using var fallback = CreateCertificate("Fallback");
        var signer = new PdfSigner(MinimalPdf(), [Zone("a"), Zone("b"), Zone("c")]);
        signer.AddCertificate(fallback);
        signer.AddCertificate(alice, "b");

        var signed = await signer.SignAsync();

        Assert.Equal(["CN=Alice", "CN=Fallback", "CN=Fallback"], ValidateSignatures(signed));
        Assert.All(new PdfSigner(signed).GetSignatureFields(), f => Assert.True(f.IsSigned));
    }

    [Fact]
    public async Task SignLater_AddsASignature_WithoutTouchingTheEarlierOne()
    {
        using var alice = CreateCertificate("Alice");
        using var bob = CreateCertificate("Bob");

        // Day 1: the document is rendered with two zones, only Alice signs
        var first = new PdfSigner(MinimalPdf(), [Zone("signature-area-1"), Zone("signature-area-2")]);
        first.AddCertificate(alice, "signature-area-1");
        var afterAlice = await first.SignAsync();

        Assert.Equal(["CN=Alice"], ValidateSignatures(afterAlice));
        Assert.Equal(
            [new PdfSignatureField("signature-area-1", true), new PdfSignatureField("signature-area-2", false)],
            new PdfSigner(afterAlice).GetSignatureFields());

        // Day 2: all we have is the stored PDF. No zones, no browser.
        var second = new PdfSigner(afterAlice);
        second.AddCertificate(bob, "signature-area-2");
        var afterBob = await second.SignAsync();

        Assert.Equal(["CN=Alice", "CN=Bob"], ValidateSignatures(afterBob));
        // Append-only: everything Alice signed is still there byte for byte
        Assert.Equal(afterAlice, afterBob[..afterAlice.Length]);
        Assert.All(new PdfSigner(afterBob).GetSignatureFields(), f => Assert.True(f.IsSigned));
    }

    [Fact]
    public async Task SignLater_ThreeSigners_ThreeSessions()
    {
        var names = new[] { "Alice", "Bob", "Carol" };
        var zones = names.Select((_, i) => Zone($"zone-{i}")).ToList();

        // Rendered with no certificates at all: fields are created, nothing is signed
        var pdf = await new PdfSigner(MinimalPdf(), zones).SignAsync();
        Assert.Empty(ValidateSignatures(pdf));
        Assert.Equal(3, new PdfSigner(pdf).GetSignatureFields().Count(f => !f.IsSigned));

        for (var i = 0; i < names.Length; i++)
        {
            using var cert = CreateCertificate(names[i]);
            var signer = new PdfSigner(pdf);
            signer.AddCertificate(cert, $"zone-{i}");
            pdf = await signer.SignAsync();

            Assert.Equal(names.Take(i + 1).Select(n => $"CN={n}"), ValidateSignatures(pdf));
        }
    }

    [Fact]
    public async Task SignLater_DefaultCertificate_SignsWhateverIsLeft()
    {
        using var alice = CreateCertificate("Alice");
        using var bob = CreateCertificate("Bob");
        var first = new PdfSigner(MinimalPdf(), [Zone("a"), Zone("b"), Zone("c")]);
        first.AddCertificate(alice, "b");
        var afterAlice = await first.SignAsync();

        var second = new PdfSigner(afterAlice);
        second.AddCertificate(bob);
        var afterBob = await second.SignAsync();

        Assert.Equal(["CN=Alice", "CN=Bob", "CN=Bob"], ValidateSignatures(afterBob));
    }

    [Fact]
    public async Task SigningAFieldTwice_IsRejected()
    {
        using var alice = CreateCertificate("Alice");
        var first = new PdfSigner(MinimalPdf(), [Zone("signature-area-1")]);
        first.AddCertificate(alice, "signature-area-1");
        var signed = await first.SignAsync();

        var again = new PdfSigner(signed);
        again.AddCertificate(alice, "signature-area-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => again.SignAsync());
        Assert.Contains("already signed", ex.Message);
    }

    [Fact]
    public async Task UnknownField_IsRejected_AndNamesTheAvailableOnes()
    {
        using var alice = CreateCertificate("Alice");
        var signer = new PdfSigner(MinimalPdf(), [Zone("signature-area-1")]);
        signer.AddCertificate(alice, "signature-area-9");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => signer.SignAsync());
        Assert.Contains("signature-area-9", ex.Message);
        Assert.Contains("signature-area-1", ex.Message);
    }

    [Fact]
    public async Task ReservedSignatureSize_IsEnforcedAndConfigurable()
    {
        using var alice = CreateCertificate("Alice");

        var tooSmall = new PdfSigner(MinimalPdf(), [Zone("z")]) { ReservedSignatureSize = 1024 };
        tooSmall.AddCertificate(alice, "z");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tooSmall.SignAsync());
        Assert.Contains(nameof(PdfSigner.ReservedSignatureSize), ex.Message);

        var roomy = new PdfSigner(MinimalPdf(), [Zone("z")]) { ReservedSignatureSize = 16384 };
        roomy.AddCertificate(alice, "z");
        Assert.Equal(["CN=Alice"], ValidateSignatures(await roomy.SignAsync()));
    }

    [Fact]
    public async Task RewrittenObjects_StayWellFormed_AndXrefPointsAtThem()
    {
        using var alice = CreateCertificate("Alice");
        using var bob = CreateCertificate("Bob");
        var original = MinimalPdf();

        var signer = new PdfSigner(original, [Zone("signature-area-1"), Zone("signature-area-2")]);
        signer.AddCertificate(alice, "signature-area-1");
        signer.AddCertificate(bob, "signature-area-2");
        var signed = await signer.SignAsync();
        var all = Encoding.Latin1.GetString(signed);
        var update = all[original.Length..];

        // The page and catalog are re-written; they must keep every entry, including names and strings
        Assert.Matches(@"\n3 0 obj\n<<\s*/Type /Page\b", update);
        Assert.Contains("/Tabs /S", update);
        Assert.Matches(@"\n1 0 obj\n<<\s*/Type /Catalog\b", update);
        Assert.Contains(@"/Lang (en-US \(test\))", update);
        Assert.Contains("/ID [<0A0B> <0C0D>]", update); // trailer entries are carried forward

        // Every xref entry in every update must point at "N 0 obj", and updates must chain through /Prev
        var checkedEntries = 0;
        foreach (Match section in Regex.Matches(update, @"(?<=\nxref\n|\d{5} n \n)(\d+) (\d+)\n((?:\d{10} \d{5} n \n)+)"))
        {
            var id = int.Parse(section.Groups[1].Value);
            foreach (Match entry in Regex.Matches(section.Groups[3].Value, @"(\d{10}) \d{5} n"))
            {
                Assert.StartsWith($"{id} 0 obj", all[int.Parse(entry.Groups[1].Value)..]);
                id++;
                checkedEntries++;
            }
        }

        Assert.True(checkedEntries >= 6);
        Assert.Equal(2, Regex.Matches(update, @"/Prev \d+").Count);
    }

    [Fact]
    public async Task Widget_GoesOnTheRightPage_WhenThePageTreeIsNested()
    {
        // Chromium nests pages under intermediate /Pages nodes in longer documents
        var pdf = BuildPdf(
            "<</Type /Catalog /Pages 2 0 R>>",
            "<</Type /Pages /Count 4 /Kids [3 0 R 6 0 R]>>",
            "<</Type /Pages /Parent 2 0 R /Count 2 /Kids [4 0 R 5 0 R]>>",
            "<</Type /Page /Parent 3 0 R /MediaBox [0 0 612 792]>>",
            "<</Type /Page /Parent 3 0 R /MediaBox [0 0 612 792]>>",
            "<</Type /Pages /Parent 2 0 R /Count 2 /Kids [7 0 R 8 0 R]>>",
            "<</Type /Page /Parent 6 0 R /MediaBox [0 0 612 792]>>",
            "<</Type /Page /Parent 6 0 R /MediaBox [0 0 612 792] /Annots 9 0 R>>",
            "[10 0 R]",
            "<</Type /Annot /Subtype /Link /Rect [0 0 1 1]>>");

        using var alice = CreateCertificate("Alice");
        var signer = new PdfSigner(pdf, [Zone("on-page-4", page: 4), Zone("on-page-2", page: 2)]);
        signer.AddCertificate(alice, "on-page-4");
        var signed = await signer.SignAsync();
        var update = Encoding.Latin1.GetString(signed, pdf.Length, signed.Length - pdf.Length);

        Assert.Equal(["CN=Alice"], ValidateSignatures(signed));
        // Page 4 is object 8 and keeps its existing (indirect) annotation next to the new widget
        Assert.Matches(@"\n8 0 obj\n<<[^>]*/Annots \[\s*10 0 R\s+\d+ 0 R\s*\]", update);
        Assert.Matches(@"/P 8 0 R[^>]*/T \(on-page-4\)", update);
        // Page 2 is object 5
        Assert.Matches(@"\n5 0 obj\n<<[^>]*/Annots \[\s*\d+ 0 R\s*\]", update);
        Assert.Matches(@"/P 5 0 R[^>]*/T \(on-page-2\)", update);
        // Intermediate nodes are never mistaken for pages
        Assert.DoesNotMatch(@"\n[36] 0 obj", update);
    }

    [Fact]
    public async Task ExistingForm_IsExtended_NotReplaced()
    {
        // A document that already has a form with a text field, declared inline in the catalog
        var pdf = BuildPdf(
            "<</Type /Catalog /Pages 2 0 R /AcroForm <</Fields [4 0 R] /DA (/Helv 0 Tf 0 g)>>>>",
            "<</Type /Pages /Count 1 /Kids [3 0 R]>>",
            "<</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R]>>",
            "<</Type /Annot /Subtype /Widget /FT /Tx /T (comment) /Rect [0 0 10 10] /P 3 0 R>>");

        using var alice = CreateCertificate("Alice");
        var signer = new PdfSigner(pdf, [Zone("sig")]);
        signer.AddCertificate(alice, "sig");
        var signed = await signer.SignAsync();
        var update = Encoding.Latin1.GetString(signed, pdf.Length, signed.Length - pdf.Length);

        Assert.Equal(["CN=Alice"], ValidateSignatures(signed));
        Assert.Matches(@"/DA \(/Helv 0 Tf 0 g\)[^>]*/Fields \[\s*4 0 R\s+\d+ 0 R\s*\]", update);
        Assert.Contains("/SigFlags 3", update);
        // Only the signature field is reported; the text field is not ours to sign
        Assert.Equal([new PdfSignatureField("sig", true)], new PdfSigner(signed).GetSignatureFields());
    }

    [Fact]
    public async Task CrossReferenceStreams_AreRejectedWithAClearMessage()
    {
        var body = "%PDF-1.5\n1 0 obj\n<</Type /XRef /Size 2 /Root 2 0 R /W [1 2 1] /Length 0>>\nstream\n\nendstream\nendobj\n";
        var pdf = Encoding.ASCII.GetBytes(body + "startxref\n9\n%%EOF");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new PdfSigner(pdf, [Zone("z")]).SignAsync());
        Assert.Contains("cross-reference streams", ex.Message);
    }

    [Fact]
    public async Task RazorDocument_CanBeCountersignedLater()
    {
        using var alice = CreateCertificate("Razor Signer");
        using var bob = CreateCertificate("Countersigner");
        using var stream = new MemoryStream();

        using (var doc = new PdfDocument())
        {
            doc.AddSignature("PrimarySignature", alice, 50, 50, 250, 80);
            doc.Begin(stream);
            doc.Close();
        }

        var razorPdf = stream.ToArray();
        Assert.Equal(["CN=Razor Signer"], ValidateSignatures(razorPdf));

        // A second party adds their own signature field and signs it
        var signer = new PdfSigner(razorPdf, [Zone("countersignature")]);
        signer.AddCertificate(bob, "countersignature");
        var countersigned = await signer.SignAsync();

        Assert.Equal(["CN=Razor Signer", "CN=Countersigner"], ValidateSignatures(countersigned));
    }
}
