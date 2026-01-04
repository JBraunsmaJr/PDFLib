using PDFLib.Console;
using PDFLib.Console.RPC;

namespace Benchmarks;

/// <summary>
/// This test encompasses the following:
///
/// <list type="number">
///     <item>Creating a "new page"</item>
///     <item>Invoking the HTML to PDF functionality</item>
/// </list>
///
/// This should help determine how much overhead there is when processing concurrent requests (page per request)
/// </summary>
public class PdfLib_SetupPages : IConverter
{
    private ChromiumBrowser _browser;
    private MemoryStream _memoryStream;
    private CdpPage _page;
    
    public async Task ConvertAsync(string html)
    {
        _page = await _browser.CreatePageAsync();
        await _page.PrintToPdfAsync(html, _memoryStream);
        await _memoryStream.FlushAsync();
        await _page.DisposeAsync();
    }
    public async Task IterationSetupAsync(string html)
    {
        _memoryStream = new MemoryStream();
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
        _memoryStream?.Close();
        if (_page != null)
        {
            await _page.DisposeAsync();
        }
    }

    public async Task GlobalCleanupAsync()
    {
        _browser?.Dispose();
        await Task.CompletedTask;
    }
}