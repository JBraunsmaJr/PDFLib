using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PDFLib.Chromium;

const string outputDirectory = "./out";
if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

await RunStandardSamples();
await RunWaitStrategySamples();
await RunSignatureSamples();

async Task RunSignatureSamples()
{
    Console.WriteLine("\n--- Running Signature Samples ---");
    using var browser = new ChromiumBrowser();
    await browser.StartAsync(new BrowserOptions());

    var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples", "signature-test.html");
    var html = await File.ReadAllTextAsync(file);
    
    var info = new FileInfo(file);
    var fullPath = Path.Combine(outputDirectory, info.Name.Replace(".html", ".pdf"));
    if (File.Exists(fullPath)) File.Delete(fullPath);

    Console.WriteLine($"Processing {Path.GetFileName(file)} with Signature Zones");

    await using var page = await browser.CreatePageAsync();
    await page.SetContentAsync(html);
    
    var zones = await page.GetSignatureZonesAsync();
    Console.WriteLine($"Found {zones.Count} signature zones:");
    foreach (var zone in zones)
    {
        var (x, y, w, h, p) = zone.ToPdfCoordinates();
        Console.WriteLine($"  - {zone.Id}: Page {p}, Pos ({x:F1}, {y:F1}), Size {w:F1}x{h:F1}");
    }

    using var ms = new MemoryStream();
    await page.PrintToPdfAsync(ms, true);
    var pdfBytes = ms.ToArray();

    if (zones.Count > 0)
    {
        Console.WriteLine("Signing PDF with selective certificates...");
        var signer = new PdfSigner(pdfBytes, zones);
        
        // Create test certificates
        using var rsa1 = RSA.Create(2048);
        var request1 = new CertificateRequest("cn=Test Signer 1", rsa1, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert1 = request1.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));

        using var rsa2 = RSA.Create(2048);
        var request2 = new CertificateRequest("cn=Test Signer 2", rsa2, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert2 = request2.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));

        // Assign cert1 to the first zone
        if (zones.Count > 0)
        {
            signer.AddCertificate(cert1, zones[0].Id);
            Console.WriteLine($"  - Assigned cert1 to {zones[0].Id}");
        }

        // Assign cert2 to the second zone (if it exists)
        if (zones.Count > 1)
        {
            signer.AddCertificate(cert2, zones[1].Id);
            Console.WriteLine($"  - Assigned cert2 to {zones[1].Id}");
        }

        // The remaining zones (if any) will stay unsigned
        if (zones.Count > 2)
        {
            Console.WriteLine($"  - Remaining {zones.Count - 2} zones will stay UNSIGNED");
        }
        
        pdfBytes = await signer.SignAsync();
    }

    await File.WriteAllBytesAsync(fullPath, pdfBytes);
    
    Console.WriteLine($"Output: {fullPath} | Size: {new FileInfo(fullPath).Length} bytes");
}

async Task RunStandardSamples()
{
    Console.WriteLine("--- Running Standard Samples ---");
    using var browser = new ChromiumBrowser();
    await browser.StartAsync(new BrowserOptions());

    foreach (var file in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples")))
    {
        if (file.Contains("wait")) continue;

        var html = await File.ReadAllTextAsync(file);
        Console.WriteLine($"Processing {Path.GetFileName(file)}: {html.Length} characters");

        await RenderToPdf(browser, file, html);
    }
}

async Task RunWaitStrategySamples()
{
    Console.WriteLine("\n--- Running Wait Strategy Samples ---");

    // Test JS Variable Wait
    {
        using var browser = new ChromiumBrowser();
        var options = new BrowserOptions
        {
            WaitStrategy = WaitStrategy.JavascriptVariable,
            WaitVariable = "window.readyToRender",
            WaitVariableValue = "true",
            WaitTimeoutMs = 15000
        };
        await browser.StartAsync(options);

        var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples", "js-variable-wait.html");
        var html = await File.ReadAllTextAsync(file);
        Console.WriteLine("Processing js-variable-wait.html (Strategy: JavascriptVariable)");
        await RenderToPdf(browser, file, html);
    }

    // Test Network Idle Wait
    {
        using var browser = new ChromiumBrowser();
        var options = new BrowserOptions
        {
            WaitStrategy = WaitStrategy.NetworkIdle,
            WaitTimeoutMs = 15000
        };
        await browser.StartAsync(options);

        var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples", "network-wait.html");
        var html = await File.ReadAllTextAsync(file);
        Console.WriteLine("Processing network-wait.html (Strategy: NetworkIdle)");
        await RenderToPdf(browser, file, html);
    }
}

async Task RenderToPdf(ChromiumBrowser browser, string file, string html)
{
    var info = new FileInfo(file);
    var fullPath = Path.Combine(outputDirectory, info.Name.Replace(".html", ".pdf"));
    if (File.Exists(fullPath))
        File.Delete(fullPath);

    var stopwatch = new Stopwatch();
    stopwatch.Start();

    await using var outputPdf = File.Create(fullPath);
    await using var page = await browser.CreatePageAsync();
    await page.SetContentAsync(html);
    await page.PrintToPdfAsync(outputPdf);
    await outputPdf.FlushAsync();

    stopwatch.Stop();
    var fileInfo = new FileInfo(fullPath);
    Console.WriteLine($"Output: {fullPath} | Size: {fileInfo.Length} bytes | Time: {stopwatch.ElapsedMilliseconds}ms");
}