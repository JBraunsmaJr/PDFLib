# Digital Signatures and Signature Zones

PDFLib supports detecting signature zones in HTML (via CSS classes) and signing the resulting
PDF using X509 certificates.

## HTML for Signature Zones

In your HTML, define areas for signatures using a specific ID format: `signature-area-{suffix}`.

```html
<div class="signature-zone" id="signature-area-1" style="width: 200px; height: 50px;">
    Sign here
</div>
```

## Signing the PDF

### Background

Unfortunately, due to the PDF specification, one-pass operations aren't possible.

When signing, we're telling the PDF where the hex-string will sit (e.g. "The signature starts at
byte 10,500 and is 8000 bytes long"). This is an array of numbers that tells the PDF which bytes to
hash. Looks something like `[0, 10500, 18500, 2000]`.

To calculate the cryptographic hash, we need to know the **final size** of the PDF with exact offsets which we
can't calculate until AFTER an initial pass.

### Usage

You must provide the signature zone data to the `PrintToPdfAsync` method. This is used for the visual representation.
Using the Chromium Developer Protocol, we inject the signature zone data into a JavaScript function which updates the
DOM. It also locates and returns all signature zones with their calculated positions so we can apply that data to the PDF.

After the PDF is generated, we then use the PDFSigner to sign the PDF with the provided certificates. It's important
to match the signature zone to the certificate.

```csharp
using System.Security.Cryptography.X509Certificates;

// We are mapping the signature zone ID to the visual data seen by the user
var signatureData = new Dictionary<string, (string name, string date)>();
signatureData["signature-area-1"] = ("John Doe", DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss"));

using var ms = File.Create("output.pdf");
var zones = await page.PrintToPdfAsync(html, ms, signatureData);

var pdfBytes = File.ReadAllBytes("output.pdf");

if (zones.Count > 0)
{
    var signer = new PdfSigner(pdfBytes, zones);
    
    // Load your certificate
    using var cert = new X509Certificate2("certificate.pfx", "password");
    
    // Add all the certificates that will be used to sign the PDF here
    signer.AddCertificate(cert, "signature-area-1");
    
    // Sign the PDF
    byte[] signedPdfBytes = await signer.SignAsync();
    await File.WriteAllBytesAsync("signed-output.pdf", signedPdfBytes);
}
```

### Several signers

Add one certificate per zone. Signatures are applied in the order the certificates are added, and each one
is written as its own incremental update, so every signature in the file validates.

```csharp
var signer = new PdfSigner(pdfBytes, zones);
signer.AddCertificate(aliceCert, "signature-area-1");
signer.AddCertificate(bobCert, "signature-area-2");
byte[] signedPdfBytes = await signer.SignAsync();
```

A certificate added without a zone id is the default: it signs every zone that has no certificate of its own.

### Signing later

A signature field is created for **every** zone when the document is first passed through `PdfSigner`,
whether or not a certificate is supplied for it. Fields without a certificate are left unsigned, and anyone
can sign them later from the stored PDF alone. No browser and no zone data are needed.

```csharp
// At render time: Alice signs, Bob's field is created but left empty
var signer = new PdfSigner(pdfBytes, zones);
signer.AddCertificate(aliceCert, "signature-area-1");
await File.WriteAllBytesAsync("contract.pdf", await signer.SignAsync());

// Days later, possibly on another machine
var stored = await File.ReadAllBytesAsync("contract.pdf");
var later = new PdfSigner(stored);

foreach (var field in later.GetSignatureFields())
    Console.WriteLine($"{field.Name}: {(field.IsSigned ? "signed" : "waiting")}");

later.AddCertificate(bobCert, "signature-area-2");
await File.WriteAllBytesAsync("contract.pdf", await later.SignAsync());
```

Things to know:

* **Earlier signatures stay valid.** The file is only ever appended to. PDF readers show an earlier signature
  as valid for the revision it signed, with later signatures listed as subsequent changes.
* **Pass every zone at render time**, even those nobody signs yet. Adding new fields to a document that is
  already signed is possible, but some readers flag it as a change made after signing.
* **The visible name and date are drawn at render time.** Signing later adds the cryptographic signature;
  it does not change what is printed in the signature area.
* **A field can be signed once.** Signing an already signed field, or a name that doesn't exist, throws an
  `InvalidOperationException` that lists the available fields.
* **Long certificate chains** may not fit the default reserved space. Raise `ReservedSignatureSize`
  (default 4096 characters, about 2 KB of signature) if `SignAsync` reports that the signature doesn't fit.
* **Supported input:** PDFs with classic cross-reference tables, which is what Chromium and this library
  produce. A PDF re-saved by another tool with compressed cross-reference streams is rejected with a
  `NotSupportedException`.
