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