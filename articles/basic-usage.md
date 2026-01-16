# Basic Usage

PDFLib provides a simple way to convert HTML and CSS to PDF using a headless Chromium browser.

## Initializing the Browser

The `ChromiumBrowser` class manages the lifecycle of the Chromium process. It's recommended to maintain a single instance of the browser throughout your application's lifetime.

```csharp
using PDFLib.Chromium;

// Initialize the browser
using var browser = new ChromiumBrowser();
await browser.StartAsync(new BrowserOptions());
```

## Basic HTML to PDF

To generate a PDF, you create a new page (similar to a tab in a browser), set its content, and then print it to a PDF stream.

```csharp
var html = "<html><body><h1>Hello World</h1><p>This is a PDF generated from HTML.</p></body></html>";
string outputPath = "output.pdf";

await using var outputPdf = File.Create(outputPath);
await using var page = await browser.CreatePageAsync();


// Print to PDF
await page.PrintToPdfAsync(html, outputPdf);
await outputPdf.FlushAsync();
```

> For PDFs with **no signature** you can stream the response back to the user! Should be able to avoid
> using `MemoryStream` - as a result you won't see memory spikes since the PDF isn't being stored in memory.
> 
> For PDFs with **signature** it's unavoidable to have the PDF fully in memory. Sadly, the PDF specification
> doesn't allow for one-pass operations.

## Wait Strategies

Sometimes your HTML content depends on external resources (like images or fonts) or JavaScript execution. 
You can use `WaitStrategy` in `BrowserOptions` to ensure the page is fully loaded before rendering.

### Network Idle

Wait until there are no more than 2 network connections for at least 500ms.

```csharp
var options = new BrowserOptions
{
    WaitStrategy = WaitStrategy.NetworkIdle,
    WaitTimeoutMs = 15000 // Timeout if it takes too long
};
await browser.StartAsync(options);
```

### JavaScript Variable

Wait until a specific JavaScript variable evaluates to a certain value. 
This is useful for Single Page Applications (SPAs) or complex JavaScript-driven layouts.

```csharp
var options = new BrowserOptions
{
    WaitStrategy = WaitStrategy.JavascriptVariable,
    WaitVariable = "window.readyToRender",
    WaitVariableValue = "true",
    WaitTimeoutMs = 15000
};
await browser.StartAsync(options);
```

### Timeouts

If timing out is not desirable, you can disable timeouts altogether by setting `WaitTimeoutMs` to null.

```csharp
var options = new BrowserOptions
{
    WaitTimeoutMs = null
};
await browser.StartAsync(options);
```

## Digital Signatures and Signature Zones

PDFLib supports detecting signature zones in HTML (via CSS classes) and signing the resulting
PDF using X509 certificates.

### HTML for Signature Zones

In your HTML, define areas for signatures using a specific ID format: `signature-area-{suffix}`.

```html
<div class="signature-zone" id="signature-area-1" style="width: 200px; height: 50px;">
    Sign here
</div>
```

### Signing the PDF

Unfortunately, due to the PDF specification, one-pass operations aren't possible.

When signing, we're telling the PDF where the hex-string will sit (e.g. "The signature starts at
byte 10,500 and is 8000 bytes long"). This is an array of numbers that tells the PDF which bytes to
hash. Looks something like `[0, 10500, 18500, 2000]`.

To calculate the cryptographic hash, we need to know the **final size** of the PDF with exact offsets which we
can't calculate until AFTER an initial pass.

```csharp
using System.Security.Cryptography.X509Certificates;

// We are mapping the signature zone ID to the visual data seen by the user
var signatureData = new Dictionary<string, (string name, string date)>();
signatureData["signature-area-1"] = ("John Doe", DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss"));

using var ms = File.Create("output.pdf");
var zones = await page.PrintToPdfAsync(html, ms, true, signatureData);

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

