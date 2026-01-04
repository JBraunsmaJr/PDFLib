using PDFLib.Console;
using PDFLib.Console.RPC;

namespace Benchmarks;

public class PdfLib : IConverter
{
    private ChromiumBrowser _browser;
    private MemoryStream _memoryStream;
    private CdpPage _page;
    
    public void Convert(string html)
    {
        _page.PrintToPdfAsync(html, _memoryStream).GetAwaiter().GetResult();
        _memoryStream.FlushAsync().GetAwaiter().GetResult();
    }
    public void IterationSetup()
    {
        _memoryStream = new MemoryStream();
        _page = _browser.CreatePageAsync().GetAwaiter().GetResult();
    }

    public void GlobalSetup()
    {
        _browser = new ChromiumBrowser();
        _browser.StartAsync(new()).GetAwaiter().GetResult();
    }

    public void IterationCleanup()
    {
        _memoryStream?.Close();
        _page.DisposeAsync().GetAwaiter().GetResult();
    }

    public void GlobalCleanup()
    {
        _browser?.Dispose();
    }
}