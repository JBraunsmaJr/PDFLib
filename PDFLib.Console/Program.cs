using System.Diagnostics;
using PDFLib.Console;

using var browser = new ChromiumBrowser();
await browser.StartAsync(new BrowserOptions());

const string output = "/out";

foreach (var file in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "samples")))
{
    var html = await File.ReadAllTextAsync(file);
    Console.WriteLine($"Read sample.html: {html.Length} characters");
    
    var info = new FileInfo(file);
    var fullPath = Path.Combine(output, info.Name.Replace(".html", ".pdf"));

    if (File.Exists(fullPath))
        File.Delete(fullPath);
    var stopwatch = new Stopwatch();
    
    stopwatch.Start();
    await using var outputPdf = File.Create(fullPath);
    await using var page = await browser.CreatePageAsync();
    await page.PrintToPdfAsync(html, outputPdf);

    await outputPdf.FlushAsync();
    await outputPdf.DisposeAsync();
    stopwatch.Stop();
    
    var fileInfo = new FileInfo(fullPath);
    Console.WriteLine($"Output file size: {fileInfo.Length} bytes | Time: {stopwatch.ElapsedMilliseconds}ms");
}

