using System.Security.Cryptography;
using System.Text.Json;
using PDFLib.Chromium.RPC;

namespace PDFLib.Chromium.Tests;

public class CdpPageTests
{
    /// <summary>Serves <paramref name="pdf"/> the way Chromium does: printToPDF returns a handle, IO.read returns base64 chunks.</summary>
    private static FakeCdpServer PdfServer(byte[] pdf, List<string>? log = null)
    {
        var position = 0;
        return new FakeCdpServer((_, method, p) =>
        {
            lock (pdf)
            {
                log?.Add(method);
                switch (method)
                {
                    case "Page.getFrameTree":
                        return Task.FromResult<string?>("""{"frameTree":{"frame":{"id":"F1"}}}""");
                    case "Runtime.evaluate":
                        return Task.FromResult<string?>("""{"result":{"type":"string","value":"complete"}}""");
                    case "Page.printToPDF":
                        return Task.FromResult<string?>("""{"stream":"h1"}""");
                    case "IO.read":
                        var size = Math.Min(p.GetProperty("size").GetInt32(), pdf.Length - position);
                        var chunk = Convert.ToBase64String(pdf, position, size);
                        position += size;
                        var eof = position >= pdf.Length ? "true" : "false";
                        return Task.FromResult<string?>($$"""{"base64Encoded":true,"data":"{{chunk}}","eof":{{eof}}}""");
                    default:
                        return Task.FromResult<string?>("{}");
                }
            }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(256 * 1024)]         // exactly one chunk
    [InlineData(256 * 1024 + 1)]
    [InlineData(5 * 1024 * 1024 + 123)]
    public async Task PrintToPdf_StreamsEveryByteIntact(int length)
    {
        var pdf = RandomNumberGenerator.GetBytes(length);
        await using var server = PdfServer(pdf);
        var page = new CdpPage(server.Dispatcher, "S1", "T1", new SemaphoreSlim(1), new BrowserOptions());

        using var output = new MemoryStream();
        await page.PrintToPdfAsync("<html><body>hi</body></html>", output).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(pdf, output.ToArray());
    }

    [Fact]
    public async Task SetContent_WaitsForLoad_WithDefaultOptions()
    {
        // Regression: the default timeout evaluated to 0ms, so the readiness check never ran at all
        var log = new List<string>();
        await using var server = PdfServer([], log);
        var page = new CdpPage(server.Dispatcher, "S1", "T1", new SemaphoreSlim(1), new BrowserOptions());

        await page.SetContentAsync("<html></html>").WaitAsync(TimeSpan.FromSeconds(10));

        lock (log) Assert.Contains("Runtime.evaluate", log);
    }

    [Fact]
    public async Task ConcurrentRenders_DoNotMixUpResponses()
    {
        // With the old shared response handler, replies to concurrent commands crossed over
        var pdfA = RandomNumberGenerator.GetBytes(700_000);
        await using var server = PdfServer(pdfA);
        var gate = new SemaphoreSlim(8);

        // Hammer the dispatcher with unrelated commands while a PDF streams
        using var cts = new CancellationTokenSource();
        var noise = Enumerable.Range(0, 8).Select(async n =>
        {
            while (!cts.IsCancellationRequested)
            {
                using var doc = await server.Dispatcher.SendCommandAsync("Page.getFrameTree");
                Assert.Equal("F1", doc!.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString());
            }
        }).ToArray();

        var page = new CdpPage(server.Dispatcher, "S1", "T1", gate, new BrowserOptions());
        using var output = new MemoryStream();
        await page.PrintToPdfAsync("<html></html>", output).WaitAsync(TimeSpan.FromSeconds(30));
        cts.Cancel();
        await Task.WhenAll(noise).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(pdfA, output.ToArray());
    }
}
