using System.Text.Json;
using System.Collections.Concurrent;
using System.Buffers;
using System.Buffers.Text;

namespace PDFLib.Console.RPC;

/// <summary>
/// Chrome DevTools Protocol (CDP) page for interacting with a specific browser tab.
/// </summary>
public class CdpPage : IAsyncDisposable
{
    private readonly CdpDispatcher _dispatcher;
    private readonly string _sessionId;
    private readonly string _targetId;
    private readonly SemaphoreSlim _semaphore;
    private readonly BrowserOptions _options;

    private class IoReadResponseHandler : CdpDispatcher.IResponseHandler
    {
        private readonly Stream _destinationStream;
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> Task => _tcs.Task;

        public IoReadResponseHandler(Stream destinationStream)
        {
            _destinationStream = destinationStream;
        }

        public void Handle(ReadOnlySequence<byte> message)
        {
            try
            {
                var reader = new Utf8JsonReader(message);
                var eof = false;
                var base64Encoded = false;
                var hasError = false;
                string? errorText = null;

                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    
                    var propertySpan = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
                    reader.Read();
                        
                    if (propertySpan.SequenceEqual(ErrorBytes))
                    {
                        hasError = true;
                        errorText = reader.TokenType == JsonTokenType.String ? reader.GetString() : "Complex error object";
                    }
                    else if (propertySpan.SequenceEqual(ResultBytes))
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName) continue;
                            
                            var resultPropSpan = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
                            reader.Read();
                                
                            if (resultPropSpan.SequenceEqual(EofBytes))
                            {
                                eof = reader.GetBoolean();
                            }
                            else if (resultPropSpan.SequenceEqual(Base64EncodedBytes))
                            {
                                base64Encoded = reader.GetBoolean();
                            }
                            else if (resultPropSpan.SequenceEqual(DataBytes))
                            {
                                if (base64Encoded)
                                {
                                    // Decode directly from the reader's buffer to avoid string allocation
                                    if (!reader.HasValueSequence)
                                    {
                                        // Fast path - data is contiguous
                                        var span = reader.ValueSpan;
                                        var maxByteCount = ((span.Length + 3) / 4) * 3;
                                        var buffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
                                        try
                                        {
                                            var result = Base64.DecodeFromUtf8(span, buffer, out _, out var written);
                                            if (result == OperationStatus.Done)
                                            {
                                                _destinationStream.Write(buffer, 0, written);
                                            }
                                            else
                                            {
                                                // Fallback
                                                var bytes = reader.GetBytesFromBase64();
                                                _destinationStream.Write(bytes);
                                            }
                                        }
                                        finally
                                        {
                                            ArrayPool<byte>.Shared.Return(buffer);
                                        }
                                    }
                                    else
                                    {
                                        // Slow path - data spans multiple segments
                                        var bytes = reader.GetBytesFromBase64();
                                        _destinationStream.Write(bytes);
                                    }
                                }
                                else
                                {
                                    // Non-base64 data (unlikely for PDF chunks)
                                    var bytes = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
                                    _destinationStream.Write(bytes);
                                }
                            }
                        }
                    }
                }

                if (hasError)
                {
                    _tcs.TrySetException(new Exception($"CDP Error: {errorText}"));
                }
                else
                {
                    _tcs.TrySetResult(eof);
                }
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }

        public void SetException(Exception ex) => _tcs.TrySetException(ex);
    }

    private string? _cachedFrameId;
    private bool _pageEnabled;
    private bool _networkEnabled;

    public CdpPage(CdpDispatcher dispatcher, string sessionId, string targetId, SemaphoreSlim semaphore, BrowserOptions options)
    {
        _dispatcher = dispatcher;
        _sessionId = sessionId;
        _targetId = targetId;
        _options = options;
        _semaphore = semaphore;
    }

    private int _chunksSinceMemoryCheck = 0;

    private async Task CheckMemoryPressure()
    {
        // Only check every 10 chunks to reduce overhead
        if (++_chunksSinceMemoryCheck < 10) return;
        _chunksSinceMemoryCheck = 0;

        var memoryInfo = GC.GetGCMemoryInfo();
        var availableRamMb = (memoryInfo.TotalAvailableMemoryBytes - memoryInfo.MemoryLoadBytes) / 1024 / 1024;

        if (availableRamMb < _options.MemoryThresholdMb)
        {
            // Non-blocking GC collect to help out
            GC.Collect(2, GCCollectionMode.Optimized, false);
            await Task.Yield();
        }
    }

    /// <summary>
    /// Sets the content of the page to the provided HTML string.
    /// </summary>
    /// <param name="html"></param>
    public async Task SetContentAsync(string html)
    {
        if (!_pageEnabled)
        {
            await _dispatcher.SendCommandAsync("Page.enable", null, _sessionId);
            _pageEnabled = true;
        }

        if (_cachedFrameId == null)
        {
            var frameTree = await _dispatcher.SendCommandAsync("Page.getFrameTree", null, _sessionId);
            _cachedFrameId = frameTree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        }

        if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle) && !_networkEnabled)
        {
            await _dispatcher.SendCommandAsync("Network.enable", null, _sessionId);
            _networkEnabled = true;
        }

        await _dispatcher.SendCommandAsync("Page.setDocumentContent", new { frameId = _cachedFrameId, html }, _sessionId);

        var startTime = DateTime.UtcNow;
        var timeout = _options.WaitTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(_options.WaitTimeoutMs.Value) : (TimeSpan?)null;

        await WaitForConditionsAsync(startTime, timeout);
    }

    // Reusable handlers to avoid lambda allocations
    private sealed class NetworkIdleHandler
    {
        private readonly ConcurrentDictionary<string, byte> _activeRequests = new();
        public int ActiveCount => _activeRequests.Count;

        public void OnRequestStarted(JsonElement p)
        {
            if (p.TryGetProperty("requestId", out var reqId))
                _activeRequests.TryAdd(reqId.GetString()!, 0);
        }

        public void OnRequestFinished(JsonElement p)
        {
            if (p.TryGetProperty("requestId", out var reqId))
                _activeRequests.TryRemove(reqId.GetString()!, out _);
        }

        public void Clear() => _activeRequests.Clear();
    }

    private async Task WaitForConditionsAsync(DateTime startTime, TimeSpan? timeout)
    {
        if (_options.WaitStrategy == 0) return;

        NetworkIdleHandler? networkHandler = null;

        if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle))
        {
            networkHandler = new NetworkIdleHandler();
            _dispatcher.On("Network.requestWillBeSent", networkHandler.OnRequestStarted);
            _dispatcher.On("Network.loadingFinished", networkHandler.OnRequestFinished);
            _dispatcher.On("Network.loadingFailed", networkHandler.OnRequestFinished);
        }

        try
        {
            var lastActiveTime = DateTime.UtcNow;
            while (timeout == null || DateTime.UtcNow - startTime < timeout.Value)
            {
                // Check Load strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.Load))
                {
                    var readyStateRes = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = "document.readyState" }, _sessionId);
                    if (readyStateRes.TryGetProperty(ResultBytes, out var result) && result.TryGetProperty(ValueBytes, out var value))
                    {
                        if (value.ValueEquals("complete"u8)) return;
                    }
                }

                // Check NetworkIdle strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle) && networkHandler != null)
                {
                    if (networkHandler.ActiveCount <= 2)
                    {
                        if (DateTime.UtcNow - lastActiveTime > TimeSpan.FromMilliseconds(500))
                        {
                            return;
                        }
                    }
                    else
                    {
                        lastActiveTime = DateTime.UtcNow;
                    }
                }

                // Check JavascriptVariable strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.JavascriptVariable) && !string.IsNullOrEmpty(_options.WaitVariable))
                {
                    var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = _options.WaitVariable }, _sessionId);
                    if (res.TryGetProperty(ResultBytes, out var result) && result.TryGetProperty(ValueBytes, out var value))
                    {
                        var match = value.ValueKind switch
                        {
                            JsonValueKind.String => value.ValueEquals(_options.WaitVariableValue),
                            JsonValueKind.True => _options.WaitVariableValue == "true",
                            JsonValueKind.False => _options.WaitVariableValue == "false",
                            JsonValueKind.Number => value.GetRawText() == _options.WaitVariableValue,
                            _ => false
                        };
                        if (match) return;
                    }
                }

                await Task.Delay(50);
            }
        }
        finally
        {
            if (networkHandler != null)
            {
                _dispatcher.Off("Network.requestWillBeSent", networkHandler.OnRequestStarted);
                _dispatcher.Off("Network.loadingFinished", networkHandler.OnRequestFinished);
                _dispatcher.Off("Network.loadingFailed", networkHandler.OnRequestFinished);
                networkHandler.Clear();
            }
        }
    }

    private static readonly byte[] ResultBytes = "result"u8.ToArray();
    private static readonly byte[] ValueBytes = "value"u8.ToArray();
    private static readonly byte[] ErrorBytes = "error"u8.ToArray();
    private static readonly byte[] EofBytes = "eof"u8.ToArray();
    private static readonly byte[] Base64EncodedBytes = "base64Encoded"u8.ToArray();
    private static readonly byte[] DataBytes = "data"u8.ToArray();

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
            
            if (dataProp.ValueKind == JsonValueKind.String)
            {
                var bytes = dataProp.GetBytesFromBase64();
                await destinationStream.WriteAsync(bytes, cancellationToken);
                await destinationStream.FlushAsync(cancellationToken);
            }
            _semaphore.Release();
            return;
        }

        try
        {
            await CheckMemoryPressure();
            
            var eof = false;

            // Stream chunks from Chromium
            while (!eof)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var handler = new IoReadResponseHandler(destinationStream);
                await _dispatcher.SendCommandInternalAsync("IO.read", new
                {
                    handle = streamHandle,
                    size = 1048576 // 1MB chunks
                }, _sessionId, handler, cancellationToken);

                eof = await handler.Task;
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