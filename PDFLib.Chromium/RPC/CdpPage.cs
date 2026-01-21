using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace PDFLib.Chromium.RPC;

/// <summary>
/// Chrome DevTools Protocol (CDP) page for interacting with a specific browser tab and generating PDFs.
/// </summary>
public class CdpPage : IAsyncDisposable
{
    private readonly CdpDispatcher _dispatcher;
    private readonly BrowserOptions _options;
    private readonly SemaphoreSlim _semaphore;
    private readonly string _sessionId;
    private readonly string _targetId;

    private string? _cachedFrameId;
    private bool _networkEnabled;
    private bool _pageEnabled;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="CdpPage"/> class.
    /// </summary>
    /// <param name="dispatcher">The CDP dispatcher.</param>
    /// <param name="sessionId">The CDP session ID.</param>
    /// <param name="targetId">The CDP target ID.</param>
    /// <param name="semaphore">The semaphore for controlling concurrent renders.</param>
    /// <param name="options">The browser options.</param>
    public CdpPage(CdpDispatcher dispatcher, string sessionId, string targetId, SemaphoreSlim semaphore,
        BrowserOptions options)
    {
        _dispatcher = dispatcher;
        _sessionId = sessionId;
        _targetId = targetId;
        _options = options;
        _semaphore = semaphore;
    }

    /// <summary>
    /// Closes the page and associated target.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _dispatcher.SendCommandAsync("Target.closeTarget", new { targetId = _targetId });
        }
        catch
        {
        }
    }

    private static string? _findSignatureAreasScript;

    private string FindSignatureAreasScript
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_findSignatureAreasScript)) return _findSignatureAreasScript;

            var assembly = typeof(CdpPage).Assembly;
            var resourceName = "PDFLib.Chromium.FindSignatureAreas.js";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    _findSignatureAreasScript = reader.ReadToEnd();
                    return _findSignatureAreasScript;
                }
            }

            // Fallback for local development if not embedded for some reason
            var baseDir = AppContext.BaseDirectory;
            var localPath = Path.Combine(baseDir, "FindSignatureAreas.js");
            if (File.Exists(localPath))
            {
                _findSignatureAreasScript = File.ReadAllText(localPath);
                return _findSignatureAreasScript;
            }

            throw new FileNotFoundException($"Could not find embedded resource '{resourceName}' or local file '{localPath}'.", "FindSignatureAreas.js");
        }
    }
    
    /// <summary>
    /// Leverages the Chrome DevTools Protocol to find signature zones in the current page's DOM.
    /// </summary>
    /// <param name="signatureData">Optional dictionary of signature data (name, date) to inject into the DOM.</param>
    /// <returns>A list of detected <see cref="SignatureZone"/> objects.</returns>
    private async Task<List<SignatureZone>> GetSignatureZonesAsync(Dictionary<string, (string name, string date)>? signatureData = null)
    {
        var script = FindSignatureAreasScript;

        object? arg = null;
        if (signatureData != null)
        {
            arg = signatureData.ToDictionary(k => k.Key, v => new { name = v.Value.name, date = v.Value.date });
        }

        using var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new
        {
            expression = $"({script})({JsonSerializer.Serialize(arg)})",
            returnByValue = true,
            awaitPromise = true
        }, _sessionId);

        if (res == null || !res.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("value", out var value))
            return new List<SignatureZone>();
        
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var zones = JsonSerializer.Deserialize<List<SignatureZone>>(value.GetRawText(), options);
        
        return zones ?? new List<SignatureZone>();
    }

    /// <summary>
    /// Sets the HTML content and prints the page to a PDF stream.
    /// </summary>
    /// <param name="html">The HTML to convert into PDF.</param>
    /// <param name="destinationStream">The stream where the PDF will be written.</param>
    /// <param name="signatureData">Optional dictionary of signature data to inject into the DOM before printing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of signature zones found in the document.</returns>
    public async Task<List<SignatureZone>> PrintToPdfAsync(string html, Stream destinationStream, Dictionary<string, (string name, string date)>? signatureData = null,
        CancellationToken cancellationToken = default)
    {
        await SetContentAsync(html);
        return await PrintToPdfAsync(destinationStream, signatureData, cancellationToken);
    }

    /// <summary>
    /// Prints the current page content to a PDF stream.
    /// </summary>
    /// <param name="destinationStream">The stream where the PDF will be written.</param>
    /// <param name="signatureData">Optional dictionary of signature data to inject into the DOM before printing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of signature zones found in the document.</returns>
    /// <exception cref="Exception">Thrown if PDF generation fails.</exception>
    public async Task<List<SignatureZone>> PrintToPdfAsync(Stream destinationStream, Dictionary<string, (string name, string date)>? signatureData = null, CancellationToken cancellationToken = default)
    {
        List<SignatureZone> zones = new();
        
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (signatureData is { Count: > 0 })
            {
                zones = await GetSignatureZonesAsync(signatureData);
            }
            
            using var resultDoc = await _dispatcher.SendCommandAsync("Page.printToPDF", new
            {
                printBackground = true,
                transferMode = "ReturnAsStream",
                preferCSSPageSize = true
            }, _sessionId, cancellationToken);

            string? streamHandle = null;
            if (resultDoc != null && resultDoc.RootElement.TryGetProperty("streamHandle", out var streamHandleProp))
                streamHandle = streamHandleProp.GetString();
            else if (resultDoc != null && resultDoc.RootElement.TryGetProperty("stream", out var streamProp))
                streamHandle = streamProp.GetString();

            if (string.IsNullOrEmpty(streamHandle))
            {
                if (resultDoc == null || !resultDoc.RootElement.TryGetProperty("data", out var dataProp)) throw new Exception("No handle or data");
                if (dataProp.ValueKind == JsonValueKind.String)
                {
                    // For small data that comes in the first response
                    var base64String = dataProp.GetString();
                    if (base64String != null)
                    {
                        var bytes = Convert.FromBase64String(base64String);
                        await destinationStream.WriteAsync(bytes, cancellationToken);
                    }
                }
                return zones;
            }

            var scratchBuffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

            try
            {
                var eof = false;

                var handler = new IoReadResponseHandler(destinationStream, scratchBuffer);

                var iteration = 0;
                while (!eof)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (iteration++ % 10 == 0) await CheckMemoryPressure();

                    // Reset the handler's TCS for the new request
                    handler.Reset();

                    await _dispatcher.SendCommandInternalAsync("IO.read", new
                    {
                        handle = streamHandle,
                        size = 256 * 1024
                    }, _sessionId, handler, cancellationToken);

                    eof = await handler.Task;
                }

                return zones;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratchBuffer);
                using var closeRes = await _dispatcher.SendCommandAsync("IO.close", new { handle = streamHandle }, _sessionId,
                    CancellationToken.None);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task CheckMemoryPressure()
    {
        var memoryInfo = GC.GetGCMemoryInfo();
        if ((memoryInfo.TotalAvailableMemoryBytes - memoryInfo.MemoryLoadBytes) / 1024 / 1024 <
            _options.MemoryThresholdMb)
        {
            if (memoryInfo.MemoryLoadBytes > memoryInfo.TotalAvailableMemoryBytes * 0.9)
            {
                GC.Collect(2, GCCollectionMode.Optimized, false);
            }
            await Task.Yield();
        }
    }

    /// <summary>
    /// Sets the content of the page to the provided HTML string.
    /// Will automatically execute the <see cref="WaitStrategy"/> and wait until the page is ready.
    /// </summary>
    /// <param name="html"></param>
    public async Task SetContentAsync(string html)
    {
        if (!_pageEnabled)
        {
            using var res = await _dispatcher.SendCommandAsync("Page.enable", null, _sessionId);
            _pageEnabled = true;
        }

        if (_cachedFrameId == null)
        {
            using var ft = await _dispatcher.SendCommandAsync("Page.getFrameTree", null, _sessionId);
            _cachedFrameId = ft?.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        }

        if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle) && !_networkEnabled)
        {
            using var res = await _dispatcher.SendCommandAsync("Network.enable", null, _sessionId);
            _networkEnabled = true;
        }

        using var resSet = await _dispatcher.SendCommandAsync("Page.setDocumentContent", new { frameId = _cachedFrameId, html },
            _sessionId);

        if (_options.WaitStrategy != 0)
        {
            await WaitForConditionsAsync(DateTime.UtcNow,
                _options.WaitTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(_options.WaitTimeoutMs.Value) : null);
        }
    }

    /// <summary>
    /// Executes a JavaScript expression and returns the result as a JsonElement.
    /// </summary>
    public async Task<JsonElement> EvaluateAsync(string expression)
    {
        var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true
        }, _sessionId);

        if (res == null) throw new Exception("No response from CDP");
        
        try
        {
            return res.RootElement.TryGetProperty("exceptionDetails", out var exception)
                ? throw new Exception($"JS Exception: {exception.GetRawText()}")
                : res.RootElement.GetProperty("result").Clone();
        }
        finally
        {
            res.Dispose();
        }
    }
    
    /// <summary>
    /// Attempts to retrieve the requestID from <paramref name="p"/>
    /// </summary>
    /// <param name="p"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    private bool TryGetRequestId(JsonElement p, out string id)
    {
        if (p.TryGetProperty("requestId", out var idProp))
        {
            id = idProp.GetString()!;
            return id != null;
        }
        id = null!;
        return false;
    }
    
    private async Task WaitForConditionsAsync(DateTime startTime, TimeSpan? timeout)
    {
        var activeRequests = new ConcurrentDictionary<string, byte>();
        Action<JsonElement>? requestStarted = null;
        Action<JsonElement>? requestFinished = null;

        if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle))
        {
            requestStarted = (p) =>
            {
                if (TryGetRequestId(p, out var id))
                {
                    activeRequests.TryAdd(id, 0);
                }
            };
            requestFinished = (p) =>
            {
                if (TryGetRequestId(p, out var id))
                {
                    activeRequests.TryRemove(id, out _);
                }
            };
            _dispatcher.On("Network.requestWillBeSent", requestStarted);
            _dispatcher.On("Network.loadingFinished", requestFinished);
            _dispatcher.On("Network.loadingFailed", requestFinished);
        }

        try
        {
            var lastActiveTime = DateTime.UtcNow;
            while (timeout == null || DateTime.UtcNow - startTime < timeout.Value)
            {
                // Check Load strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.Load))
                {
                    using var readyStateRes = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = "document.readyState" }, _sessionId);
                    if (readyStateRes != null && readyStateRes.RootElement.TryGetProperty("result"u8, out var result) && result.TryGetProperty("value"u8, out var value))
                    {
                        if (value.ValueEquals("complete"u8)) return;
                    }
                }

                // Check NetworkIdle strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle))
                {
                    if (activeRequests.Count <= 2)
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
                else if (!_options.WaitStrategy.HasFlag(WaitStrategy.JavascriptVariable))
                {
                    // No other strategy, just return
                    return;
                }

                // Check JavascriptVariable strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.JavascriptVariable) && !string.IsNullOrEmpty(_options.WaitVariable))
                {
                    using var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = _options.WaitVariable }, _sessionId);
                    if (res != null && res.RootElement.TryGetProperty("result"u8, out var result) && result.TryGetProperty("value"u8, out var value))
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
            if (requestStarted != null && requestFinished != null)
            {
                _dispatcher.Off("Network.requestWillBeSent", requestStarted);
                _dispatcher.Off("Network.loadingFinished", requestFinished);
                _dispatcher.Off("Network.loadingFailed", requestFinished);
            }
        }
    }

    /// <summary>
    ///     Reusable Handler
    /// </summary>
    /// <remarks>
    ///     Implementing IValueTaskSource could be better, but more complex
    ///     Using a reusable TCS logic via a simple Reset method is safer for now
    /// </remarks>
    private class IoReadResponseHandler : CdpDispatcher.IResponseHandler
    {
        private readonly Stream _destinationStream;
        private readonly byte[] _scratchBuffer;

        /// <remarks>
        ///     We reuse this TCS. Once a chunk is done, we await it, then create a new one for the next chunk.
        ///     (Reusing the actual TCS object is not thread-safe or recommended, swapping the reference is cheap).
        /// </remarks>
        private TaskCompletionSource<bool> _tcs;

        public IoReadResponseHandler(Stream destinationStream, byte[] scratchBuffer)
        {
            _destinationStream = destinationStream;
            _scratchBuffer = scratchBuffer;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<bool> Task => _tcs.Task;

        public async ValueTask Handle(ReadOnlySequence<byte> message)
        {
            try
            {
                var reader = new Utf8JsonReader(message);
                var eof = false;
                var base64Encoded = false;
                var hasError = false;
                string? errorText = null;
                ReadOnlySequence<byte> data = default;

                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

                    if (reader.ValueTextEquals("result"u8))
                    {
                        reader.Read();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName) continue;

                            if (reader.ValueTextEquals("data"u8))
                            {
                                reader.Read();
                                if (reader.HasValueSequence)
                                {
                                    data = reader.ValueSequence;
                                }
                                else
                                {
                                    /*
                                     * TODO: Investigate ValueSequence
                                     * If a single segment, reader.ValueSpan contains the data.
                                     * To avoid Utf8JsonReader limitations, we can't easily get a ReadOnlySequence
                                     * from a Span without a copy if we want to avoid pinning.
                                     * Since we're in a high-performance path, we'll use `ToArray` for now.
                                     * We know that for large data, it will likely be a ValueSequence.
                                     */
                                    data = new ReadOnlySequence<byte>(reader.ValueSpan.ToArray());
                                }
                            }
                            else if (reader.ValueTextEquals("eof"u8))
                            {
                                reader.Read();
                                eof = reader.GetBoolean();
                            }
                            else if (reader.ValueTextEquals("base64Encoded"u8))
                            {
                                reader.Read();
                                base64Encoded = reader.GetBoolean();
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                    else if (reader.ValueTextEquals("error"u8))
                    {
                        reader.Read();
                        hasError = true;
                        errorText = reader.GetString();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                if (hasError)
                {
                    _tcs.TrySetException(new Exception($"CDP Error: {errorText}"));
                    return;
                }

                if (!data.IsEmpty)
                {
                    await ProcessDataAsync(data, base64Encoded);
                }
                
                _tcs.TrySetResult(eof);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }

        public void SetException(Exception ex)
        {
            _tcs.TrySetException(ex);
        }
        
        /// <summary>
        /// Prepares the handler for the next chunk of data.
        /// </summary>
        public void Reset()
        {
            if (_tcs.Task.IsCompleted)
                _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        
        private const int PayloadBufferThreshold = 512 * 1024;
        
        private async Task ProcessDataAsync(ReadOnlySequence<byte> data, bool isBase64)
        {
            if (isBase64)
            {
                var payloadLength = (int)data.Length;
                
                if (payloadLength > PayloadBufferThreshold)
                {
                    var input = ArrayPool<byte>.Shared.Rent(payloadLength);
                    var output = ArrayPool<byte>.Shared.Rent(Base64.GetMaxDecodedFromUtf8Length(payloadLength));
                    try
                    {
                        data.CopyTo(input);
                        
                        var status = Base64.DecodeFromUtf8(input.AsSpan(0, payloadLength), output, out _, out var written);
                        if (status == OperationStatus.Done)
                        {
                            await _destinationStream.WriteAsync(output.AsMemory(0, written));
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(input);
                        ArrayPool<byte>.Shared.Return(output);
                    }

                    return;
                }

                data.CopyTo(_scratchBuffer);
                
                var status2 = Base64.DecodeFromUtf8(_scratchBuffer.AsSpan(0, payloadLength), _scratchBuffer.AsSpan(512 * 1024), out _, out var written2);

                if (status2 == OperationStatus.Done)
                {
                    await _destinationStream.WriteAsync(_scratchBuffer.AsMemory(512 * 1024, written2));
                }
            }
            else
            {
                if (data.IsSingleSegment)
                {
                    await _destinationStream.WriteAsync(data.First);
                }
                else
                {
                    if (data.Length <= _scratchBuffer.Length)
                    {
                        data.CopyTo(_scratchBuffer);
                        await _destinationStream.WriteAsync(_scratchBuffer.AsMemory(0, (int)data.Length));
                    }
                    else
                    {
                        foreach (var segment in data)
                        {
                            await _destinationStream.WriteAsync(segment);
                        }
                    }
                }
            }
        }
    }
}