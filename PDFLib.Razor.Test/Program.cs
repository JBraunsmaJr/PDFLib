using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PDFLib;
using PDFLib.Razor.Test;
using SkiaSharp;

var imagePath = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "test.png");

// Ensure test.png exists (from previous runs or create a dummy one)
if (!File.Exists(imagePath))
{
    using var bitmap = new SKBitmap(100, 100);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Blue);
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    await using var stream = File.OpenWrite(imagePath);
    data.SaveTo(stream);
}

// Generate a dummy certificate for signing
using var rsa = RSA.Create(2048);
var request = new CertificateRequest("cn=Test Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var cert = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));

var items = new List<InvoiceComponent.InvoiceItem>
{
    new() { Name = "Consulting Services", Price = 1500.00m },
    new() { Name = "Software License", Price = 499.99m },
    new() { Name = "Hardware Support", Price = 250.00m }
};

var parameters = new Dictionary<string, object?>
{
    { "InvoiceId", 12345 },
    { "Items", items },
    { "LogoPath", imagePath }
};

using var doc = new PdfDocument();
doc.AddSignature("PrimarySignature", cert); // Register certificate with name matching Razor

await using var fs = new FileStream(Path.Join(AppDomain.CurrentDomain.BaseDirectory, "razor-test.pdf"), FileMode.Create);
doc.Begin(fs);

var renderer = new PdfRenderer();
await renderer.RenderToDocumentAsync<InvoiceComponent>(doc, parameters);

doc.Close();

Console.WriteLine("Razor-based PDF saved to razor-test.pdf");