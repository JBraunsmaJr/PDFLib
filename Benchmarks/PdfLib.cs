using PDFLib.Chromium;

namespace Benchmarks;

/// <summary>
///     This test is slim, in which it ONLY tests the HTML to PDF component of PDFLib.
/// </summary>
/// <remarks>
///     The memory consumption will be relatively higher than webkit/dink because of the base64 encoded data. However,
///     the goal is to support streaming. Since we process 1MB at a time, in theory we shouldn't surpass 1MB (by much? if
///     at all?)
/// </remarks>
public class PdfLib : IConverter
{
    private ChromiumBrowser _browser;
    private FileStream _fileStream;

    public async Task ConvertAsync(string html)
    {
        var page = await _browser.CreatePageAsync();
        try
        {
            await page.SetContentAsync(html);
            await page.PrintToPdfAsync(_fileStream);
            await _fileStream.FlushAsync();
        }
        finally
        {
            await page.DisposeAsync();
        }
    }

    public async Task IterationSetupAsync(string html)
    {
        /*
            Memory stream is impacting our allocations during testing... 
            By using a file, things are immediately flushed to disk instead so we avoid memory pressure.
         */
        _fileStream = File.Create("test.pdf");
    }

    public async Task GlobalSetupAsync()
    {
        _browser = ChromiumBrowser.Instance;
        await _browser.StartAsync(new BrowserOptions
        {
            // Set memory threshold to 0 to disable memory pressure checks during benchmarks
            MemoryThresholdMb = 0
        });
    }

    public async Task IterationCleanupAsync()
    {
        _fileStream?.Close();
        if(File.Exists("test.pdf"))
            File.Delete("test.pdf");
    }

    public async Task GlobalCleanupAsync()
    {
        //_browser?.Dispose();
        await Task.CompletedTask;
    }
}