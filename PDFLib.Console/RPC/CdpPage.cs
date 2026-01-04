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

    public async Task SetContentAsync(string html)
    {
        await _dispatcher.SendCommandAsync("Page.enable", null, _sessionId);
        var frameTree = await _dispatcher.SendCommandAsync("Page.getFrameTree", null, _sessionId);
        var frameId = frameTree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        await _dispatcher.SendCommandAsync("Page.setDocumentContent", new { frameId, html }, _sessionId);
        
        // TODO: This is a potential issue, could take a bit of time for page to actually become ready
        for (var i = 0; i < 50; i++)
        {
            var readyStateRes = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = "document.readyState" }, _sessionId);
            var readyState = readyStateRes.GetProperty("result").GetProperty("value").GetString();
            if (readyState == "complete") break;
            await Task.Delay(200);
        }
    }

    public async Task PrintToPdfAsync(string html, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        await SetContentAsync(html);
        await PrintToPdfAsync(destinationStream, cancellationToken);
    }

    public async Task PrintToPdfAsync(Stream destinationStream, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        
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