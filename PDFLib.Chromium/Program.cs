using System.Diagnostics;
using PDFLib.Chromium;

const string OUTPUT_DIRECTORY = "/out";
if (!Directory.Exists(OUTPUT_DIRECTORY)) Directory.CreateDirectory(OUTPUT_DIRECTORY);

await RunStandardSamples();
await RunWaitStrategySamples();

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
    var fullPath = Path.Combine(OUTPUT_DIRECTORY, info.Name.Replace(".html", ".pdf"));
    if (File.Exists(fullPath))
        File.Delete(fullPath);
        
    var stopwatch = new Stopwatch();
    stopwatch.Start();
    
    await using var outputPdf = File.Create(fullPath);
    await using var page = await browser.CreatePageAsync();
    await page.PrintToPdfAsync(html, outputPdf);
    await outputPdf.FlushAsync();
    
    stopwatch.Stop();
    var fileInfo = new FileInfo(fullPath);
    Console.WriteLine($"Output: {fullPath} | Size: {fileInfo.Length} bytes | Time: {stopwatch.ElapsedMilliseconds}ms");
}

