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

    private string FindSignatureAreasScript
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(field)) return field;
            field = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FindSignatureAreas.js"));
            return field;
        }
    }
    
    /// <summary>
    /// Leverages the Chrome DevTools Protocol to find signature zones in the current page's DOM.
    /// </summary>
    /// <param name="signatureData">Optional dictionary of signature data (name, date) to inject into the DOM.</param>
    /// <returns>A list of detected <see cref="SignatureZone"/> objects.</returns>
    public async Task<List<SignatureZone>> GetSignatureZonesAsync(Dictionary<string, (string name, string date)>? signatureData = null)
    {
        var script = FindSignatureAreasScript;

        object? arg = null;
        if (signatureData != null)
        {
            arg = signatureData.ToDictionary(k => k.Key, v => new { name = v.Value.name, date = v.Value.date });
        }

        var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new
        {
            expression = $"({script})({JsonSerializer.Serialize(arg)})",
            returnByValue = true,
            awaitPromise = true
        }, _sessionId);

        if (!res.TryGetProperty("result", out var result) || !result.TryGetProperty("value", out var value))
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
    /// <param name="includeZones">Whether to fetch and return signature zones.</param>
    /// <param name="signatureData">Optional dictionary of signature data to inject into the DOM before printing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of signature zones found in the document.</returns>
    public async Task<List<SignatureZone>> PrintToPdfAsync(string html, Stream destinationStream,
        bool includeZones = false, Dictionary<string, (string name, string date)>? signatureData = null,
        CancellationToken cancellationToken = default)
    {
        await SetContentAsync(html);
        return await PrintToPdfAsync(destinationStream, includeZones, signatureData, cancellationToken);
    }
    
    /// <summary>
    /// Prints the current page content to a PDF stream.
    /// </summary>
    /// <param name="destinationStream">The stream where the PDF will be written.</param>
    /// <param name="includeZones">Whether to fetch and return signature zones.</param>
    /// <param name="signatureData">Optional dictionary of signature data to inject into the DOM before printing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of signature zones found in the document.</returns>
    /// <exception cref="Exception">Thrown if PDF generation fails.</exception>
    public async Task<List<SignatureZone>> PrintToPdfAsync(Stream destinationStream, bool includeZones = false, Dictionary<string, (string name, string date)>? signatureData = null, CancellationToken cancellationToken = default)
    {
        List<SignatureZone> zones = new();
        
        await _semaphore.WaitAsync(cancellationToken);

        // 1MB buffer handles 256KB chunks comfortably
        var scratchBuffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

        try
        {
            if (includeZones || signatureData != null)
            {
                zones = await GetSignatureZonesAsync(signatureData);
            }
            
            var result = await _dispatcher.SendCommandAsync("Page.printToPDF", new
            {
                printBackground = true,
                transferMode = "ReturnAsStream",
                preferCSSPageSize = true
            }, _sessionId, cancellationToken);

            string? streamHandle = null;
            if (result.TryGetProperty("streamHandle", out var streamHandleProp))
                streamHandle = streamHandleProp.GetString();
            else if (result.TryGetProperty("stream", out var streamProp))
                streamHandle = streamProp.GetString();

            if (string.IsNullOrEmpty(streamHandle))
            {
                if (!result.TryGetProperty("data", out var dataProp)) throw new Exception("No handle or data");
                if (dataProp.ValueKind == JsonValueKind.String && dataProp.TryGetBytesFromBase64(out var bytes))
                    await destinationStream.WriteAsync(bytes, cancellationToken);
                return zones;
            }

            try
            {
                await CheckMemoryPressure();
                var eof = false;

                var handler = new IoReadResponseHandler(destinationStream, scratchBuffer);

                while (!eof)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Reset the handler's TCS for the new request
                    handler.Reset();

                    await _dispatcher.SendCommandInternalAsync("IO.read", new
                    {
                        handle = streamHandle,
                        size = 256 * 1024
                    }, _sessionId, handler, cancellationToken);

                    eof = await handler.Task;
                }

                await destinationStream.FlushAsync(cancellationToken);
                return zones;
            }
            finally
            {
                await _dispatcher.SendCommandAsync("IO.close", new { handle = streamHandle }, _sessionId,
                    CancellationToken.None);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratchBuffer);
            _semaphore.Release();
        }
    }

    private async Task CheckMemoryPressure()
    {
        var memoryInfo = GC.GetGCMemoryInfo();
        if ((memoryInfo.TotalAvailableMemoryBytes - memoryInfo.MemoryLoadBytes) / 1024 / 1024 <
            _options.MemoryThresholdMb)
        {
            GC.Collect(2, GCCollectionMode.Optimized, false);
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
            await _dispatcher.SendCommandAsync("Page.enable", null, _sessionId);
            _pageEnabled = true;
        }

        if (_cachedFrameId == null)
        {
            var ft = await _dispatcher.SendCommandAsync("Page.getFrameTree", null, _sessionId);
            _cachedFrameId = ft.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        }

        if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle) && !_networkEnabled)
        {
            await _dispatcher.SendCommandAsync("Network.enable", null, _sessionId);
            _networkEnabled = true;
        }

        await _dispatcher.SendCommandAsync("Page.setDocumentContent", new { frameId = _cachedFrameId, html },
            _sessionId);
        await WaitForConditionsAsync(DateTime.UtcNow,
            _options.WaitTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(_options.WaitTimeoutMs.Value) : null);
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

        if (res.TryGetProperty("exceptionDetails", out var exception))
        {
            throw new Exception($"JS Exception: {exception.GetRawText()}");
        }

        return res.GetProperty("result");
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
        if (_options.WaitStrategy == 0) return;

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
                    var readyStateRes = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = "document.readyState" }, _sessionId);
                    if (readyStateRes.TryGetProperty("result"u8, out var result) && result.TryGetProperty("value"u8, out var value))
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

                // Check JavascriptVariable strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.JavascriptVariable) && !string.IsNullOrEmpty(_options.WaitVariable))
                {
                    var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = _options.WaitVariable }, _sessionId);
                    if (res.TryGetProperty("result"u8, out var result) && result.TryGetProperty("value"u8, out var value))
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

                    if (reader.ValueTextEquals("result"u8))
                    {
                        reader.Read();
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (reader.TokenType != JsonTokenType.PropertyName) continue;

                            if (reader.ValueTextEquals("data"u8))
                            {
                                reader.Read();
                                ProcessData(ref reader, base64Encoded);
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
                    _tcs.TrySetException(new Exception($"CDP Error: {errorText}"));
                else
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

        private void ProcessData(ref Utf8JsonReader reader, bool isBase64)
        {
            if (isBase64)
            {
                var payloadLength =
                    reader.HasValueSequence ? (int)reader.ValueSequence.Length : reader.ValueSpan.Length;
                var halfBuffer = _scratchBuffer.Length / 2;

                if (payloadLength > halfBuffer)
                {
                    // Fallback should strictly never happen with 256KB chunks vs 1MB buffer
                    var input = ArrayPool<byte>.Shared.Rent(payloadLength);
                    try
                    {
                        if (reader.HasValueSequence) reader.ValueSequence.CopyTo(input);
                        else reader.ValueSpan.CopyTo(input);
                        if (reader.TryGetBytesFromBase64(out var bytes)) _destinationStream.Write(bytes);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(input);
                    }

                    return;
                }

                var inputSpan = _scratchBuffer.AsSpan(0, payloadLength);
                if (reader.HasValueSequence) reader.ValueSequence.CopyTo(inputSpan);
                else reader.ValueSpan.CopyTo(inputSpan);

                var outputSpan = _scratchBuffer.AsSpan(halfBuffer);
                var status = Base64.DecodeFromUtf8(inputSpan, outputSpan, out _, out var written);

                if (status == OperationStatus.Done)
                    _destinationStream.Write(_scratchBuffer, halfBuffer, written);
                else if (reader.TryGetBytesFromBase64(out var bytes))
                    _destinationStream.Write(bytes);
            }
            else
            {
                if (reader.HasValueSequence)
                    foreach (var segment in reader.ValueSequence)
                        _destinationStream.Write(segment.Span);
                else
                    _destinationStream.Write(reader.ValueSpan);
            }
        }
    }
}