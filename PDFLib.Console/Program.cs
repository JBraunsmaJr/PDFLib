using PDFLib.Console;

using var browser = new ChromiumBrowser();
await browser.StartAsync(new BrowserOptions());

var sampleHtml = await File.ReadAllTextAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.html"));
Console.WriteLine($"Read sample.html: {sampleHtml.Length} characters");

const string output = "/out/sample.pdf";

if (File.Exists(output))
    File.Delete(output);

await using var outputPdf = File.Create(output);
await using var page = await browser.CreatePageAsync();
await page.PrintToPdfAsync(sampleHtml, outputPdf);

await outputPdf.FlushAsync();
await outputPdf.DisposeAsync();
var fileInfo = new FileInfo(output);
Console.WriteLine($"Output file size: {fileInfo.Length} bytes");