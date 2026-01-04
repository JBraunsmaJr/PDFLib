using System.Text;

namespace PDFLib.Console.RPC;

public class CdpPage : IAsyncDisposable
{
    private readonly CdpDispatcher _dispatcher;
    private readonly string _sessionId;
    private readonly string _targetId;
    private readonly SemaphoreSlim _semaphore;
    private readonly BrowserOptions _options;

    public CdpPage(CdpDispatcher dispatcher, string sessionId, string targetId, SemaphoreSlim semaphore, BrowserOptions options)
    {
        _dispatcher = dispatcher;
        _sessionId = sessionId;
        _targetId = targetId;
        _options = options;
        _semaphore = semaphore;
    }

    private async Task CheckMemoryPressure(CancellationToken cancellationToken)
    {
        var memoryInfo = GC.GetGCMemoryInfo();
        var availableRamMb = (memoryInfo.TotalAvailableMemoryBytes - memoryInfo.MemoryLoadBytes) / 1024 / 1024;

        if (availableRamMb < _options.MemoryThresholdMb)
        {
            // Non-blocking GC collect to help out
            GC.Collect(2, GCCollectionMode.Optimized, false);
            await Task.Delay(1000, cancellationToken);
        }
    }

    private async Task SetContentAsync(string html)
    {
        await _dispatcher.SendCommandAsync("Page.enable", null, _sessionId);
        var frameTree = await _dispatcher.SendCommandAsync("Page.getFrameTree", null, _sessionId);
        var frameId = frameTree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        await _dispatcher.SendCommandAsync("Page.setDocumentContent", new { frameId, html }, _sessionId);
        
        // Wait for the page to be "ready"
        for (int i = 0; i < 50; i++)
        {
            var readyStateRes = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = "document.readyState" }, _sessionId);
            var readyState = readyStateRes.GetProperty("result").GetProperty("value").GetString();
            if (readyState == "complete") break;
            await Task.Delay(200);
        }
    }

    /// <remarks>
    /// To avoid loading a potentially large PDF info memory, we'll directly
    /// write to a destination stream which can be streamed/chunkcated on the fly to consumer.
    /// 
    /// We do NOT want to load the PDF in its entirety into memory. This would cause memory spikes which we're trying to avoid, especially for larger
    /// documents (which might cause an OOM issue)
    /// </remarks>
    /// <param name="html"></param>
    /// <param name="destinationStream"></param>
    /// <param name="cancellationToken"></param>
    /// <example>
    /// <code>
    /// await using var page = await browser.CreatePageAsync();
    /// await page.SetContentAsync(largeHtml);
    /// using var fileStream = File.Create("huge-report.pdf");
    /// await page.PrintToPdfAsync(fileStream);
    /// </code>
    /// </example>
    /// <example>
    /// <code>
    /// [HttpGet("download-pdf")]
    /// public async Task GetPdf()
    /// {
    ///   Response.ContentType = "application/pdf";
    ///   Response.Headers.Add("Content-Disposition", "attachment; filename=report.pdf");
    ///   await using var page = await _browser.CreatePageAsync();
    ///   await page.PrintToPdfAsync(Response.Body);
    /// }
    /// </code>
    /// </example>
    /// <example>
    /// <code>
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    /// await page.PrintToPdfAsync(myFileStream, cts.Token); // if > 60 seconds - automatically throws
    /// </code>
    /// </example>
    /// <example>
    /// <code>
    /// [HttpGet("download-pdf")]
    /// public async Task GetPdf(CancellationToken clientClosedToken)
    /// {
    ///    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    ///    using var linkedCts = new CancellationTokenSource.CreateLinkedTokenSource(clientClosedToken, timeoutCts.Token);
    /// 
    ///    await page.PrintToPdfAsync(Response.Body, null, linkedCts.Token);
    /// }
    /// </code>
    /// </example>
    public async Task PrintToPdfAsync(string html, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        await SetContentAsync(html);
        
        // ReturnAsStream ensures we don't crash on 150+ page documents
        var result = await _dispatcher.SendCommandAsync("Page.printToPDF", new 
        { 
            printBackground = true, 
            transferMode = "ReturnAsStream",
            preferCSSPageSize = true
        }, _sessionId, cancellationToken);

        string? streamHandle = null;
        if (result.TryGetProperty("streamHandle", out var streamHandleProp))
        {
            streamHandle = streamHandleProp.GetString();
        }
        else if (result.TryGetProperty("stream", out var streamProp))
        {
            streamHandle = streamProp.GetString();
        }

        if (string.IsNullOrEmpty(streamHandle))
        {
            // If ReturnAsStream failed or was ignored, the PDF might be in 'data'
            if (!result.TryGetProperty("data", out var dataProp))
                throw new Exception(
                    $"CDP Error: Page.printToPDF did not return stream handle or data. Response: {result.GetRawText()}");
            
            var base64Data = dataProp.GetString();
            if (!string.IsNullOrEmpty(base64Data))
            {
                var bytes = Convert.FromBase64String(base64Data);
                await destinationStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                await destinationStream.FlushAsync(cancellationToken);
            }
            _semaphore.Release();
            return;
        }

        try
        {
            await CheckMemoryPressure(cancellationToken);
            
            var eof = false;

            // Stream chunks from Chromium (1MB at a time)
            while (!eof)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var readRes = await _dispatcher.SendCommandAsync("IO.read", new
                {
                    handle = streamHandle,
                    size = 1024 * 1024
                }, _sessionId, cancellationToken);

                eof = readRes.GetProperty("eof").GetBoolean();
                var data = readRes.GetProperty("data").GetString();

                if (string.IsNullOrEmpty(data)) continue;

                byte[] bytes;
                if (readRes.TryGetProperty("base64Encoded", out var encodedProp) && encodedProp.GetBoolean())
                {
                    bytes = Convert.FromBase64String(data);
                }
                else
                {
                    bytes = Encoding.UTF8.GetBytes(data);
                }

                await destinationStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
            await destinationStream.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // TODO: Add some form of logging
            throw;
        }
        finally
        {
            // Cleanup handle
            // Using CancellationToken.None due to potential invalid/expired token.
            await _dispatcher.SendCommandAsync("IO.close", new { handle = streamHandle }, _sessionId, CancellationToken.None);
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try {
            await _dispatcher.SendCommandAsync("Target.closeTarget", new { targetId = _targetId });
        } catch { /* Ignore if browser already closed */ }
    }
}