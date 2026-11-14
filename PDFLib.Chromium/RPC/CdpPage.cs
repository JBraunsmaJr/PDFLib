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

    /// <summary>Number of times this page has been handed out by the browser's page pool.</summary>
    internal int RentCount { get; set; }

    /// <summary>
    /// Navigates to a fresh about:blank document so nothing from the previous render survives.
    /// <see cref="SetContentAsync"/> alone replaces the markup but keeps the same JavaScript global object,
    /// so script state from one document would otherwise be visible to the next.
    /// </summary>
    internal async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        using var res = await _dispatcher.SendCommandAsync("Page.navigate", new { url = "about:blank" }, _sessionId,
            cancellationToken);
        if (res != null && res.RootElement.TryGetProperty("errorText", out var error))
            throw new InvalidOperationException($"Could not reset page: {error.GetString()}");
    }

    private static string? _findSignatureAreasScript;

    // JsonSerializerOptions caches type metadata; creating one per call throws that cache away every time
    private static readonly JsonSerializerOptions SignatureZoneSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
    private async Task<List<SignatureZone>> GetSignatureZonesAsync(Dictionary<string, Signature>? signatureData = null)
    {
        var script = FindSignatureAreasScript;

        object? arg = null;
        if (signatureData != null)
        {
            arg = signatureData.ToDictionary(k => k.Key, v => new { name = v.Value.Name, date = v.Value.Date });
        }

        using var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new
        {
            expression = $"({script})({JsonSerializer.Serialize(arg)})",
            returnByValue = true,
            awaitPromise = true
        }, _sessionId);

        if (res == null || !res.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("value", out var value))
            return new List<SignatureZone>();
        
        var zones = value.Deserialize<List<SignatureZone>>(SignatureZoneSerializerOptions);
        
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
    public async Task<List<SignatureZone>> PrintToPdfAsync(string html, Stream destinationStream, Dictionary<string, Signature>? signatureData = null,
        CancellationToken cancellationToken = default)
    {
        await SetContentAsync(html, cancellationToken);
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
    public async Task<List<SignatureZone>> PrintToPdfAsync(Stream destinationStream, Dictionary<string, Signature>? signatureData = null, CancellationToken cancellationToken = default)
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
                preferCSSPageSize = true,
                generateTaggedPDF = _options.GenerateTaggedPdf
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

            // A 256KB read is ~350KB of base64, decoded in place
            var scratchBuffer = ArrayPool<byte>.Shared.Rent(512 * 1024);

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
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SetContentAsync(string html, CancellationToken cancellationToken = default)
    {
        if (!_pageEnabled)
        {
            using var res = await _dispatcher.SendCommandAsync("Page.enable", null, _sessionId, cancellationToken);
            _pageEnabled = true;
        }

        if (_cachedFrameId == null)
        {
            using var ft = await _dispatcher.SendCommandAsync("Page.getFrameTree", null, _sessionId, cancellationToken);
            _cachedFrameId = ft?.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString();
        }

        if (_options.WaitStrategy.HasFlag(WaitStrategy.NetworkIdle) && !_networkEnabled)
        {
            using var res = await _dispatcher.SendCommandAsync("Network.enable", null, _sessionId, cancellationToken);
            _networkEnabled = true;
        }

        using var resSet = await _dispatcher.SendCommandWithLargeStringAsync("Page.setDocumentContent", _sessionId,
            "frameId", _cachedFrameId, "html", html, cancellationToken);

        if (_options.WaitStrategy == WaitStrategy.Load)
        {
            await WaitForLoadAsync(cancellationToken);
        }
        else if (_options.WaitStrategy != 0)
        {
            await WaitForConditionsAsync(DateTime.UtcNow,
                _options.WaitTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(_options.WaitTimeoutMs.Value) : null,
                cancellationToken);
        }
    }

    private const string WaitForLoadScript =
        "document.readyState === 'complete' ? true : new Promise(r => window.addEventListener('load', () => r(true), { once: true }))";

    /// <summary>
    /// Waits for the load event with a single round trip. Chromium answers the moment the page is loaded,
    /// instead of us polling document.readyState and sleeping 50ms between attempts.
    /// </summary>
    private async Task WaitForLoadAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.WaitTimeoutMs.HasValue) timeoutCts.CancelAfter(_options.WaitTimeoutMs.Value);

        try
        {
            using var res = await _dispatcher.SendCommandAsync("Runtime.evaluate",
                new { expression = WaitForLoadScript, awaitPromise = true, returnByValue = true },
                _sessionId, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // WaitTimeoutMs elapsed: render what we have, same as the polling strategies do
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
    
    private async Task WaitForConditionsAsync(DateTime startTime, TimeSpan? timeout, CancellationToken cancellationToken)
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
            // Scoped to this page's session: otherwise concurrent renders count each other's requests
            _dispatcher.On("Network.requestWillBeSent", _sessionId, requestStarted);
            _dispatcher.On("Network.loadingFinished", _sessionId, requestFinished);
            _dispatcher.On("Network.loadingFailed", _sessionId, requestFinished);
        }

        try
        {
            var lastActiveTime = DateTime.UtcNow;
            while (timeout == null || DateTime.UtcNow - startTime < timeout.Value)
            {
                // Check Load strategy
                if (_options.WaitStrategy.HasFlag(WaitStrategy.Load))
                {
                    using var readyStateRes = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = "document.readyState" }, _sessionId, cancellationToken);
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
                    using var res = await _dispatcher.SendCommandAsync("Runtime.evaluate", new { expression = _options.WaitVariable }, _sessionId, cancellationToken);
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

                await Task.Delay(50, cancellationToken);
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
            byte[]? rented = null;
            try
            {
                /*
                 * Parse synchronously (Utf8JsonReader can't live across an await) and copy the payload
                 * straight into a pooled buffer. Only the stream write below is asynchronous.
                 */
                var parsed = Parse(message, ref rented);
                if (parsed.Error != null)
                {
                    _tcs.TrySetException(new Exception($"CDP Error: {parsed.Error}"));
                    return;
                }

                if (parsed.Length > 0)
                    await _destinationStream.WriteAsync((rented ?? _scratchBuffer).AsMemory(0, parsed.Length));

                _tcs.TrySetResult(parsed.Eof);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
            finally
            {
                if (rented != null) ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private (int Length, bool Eof, string? Error) Parse(ReadOnlySequence<byte> message, ref byte[]? rented)
        {
            var reader = new Utf8JsonReader(message);
            var eof = false;
            var base64Encoded = false;
            var length = 0;
            string? error = null;

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
                            var encodedLength = reader.HasValueSequence
                                ? checked((int)reader.ValueSequence.Length)
                                : reader.ValueSpan.Length;

                            var target = _scratchBuffer;
                            if (encodedLength > target.Length)
                                target = rented = ArrayPool<byte>.Shared.Rent(encodedLength);

                            // Handles multi-segment values and JSON escapes without an intermediate array
                            length = reader.CopyString(target);
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
                    // "error" is an object ({code, message}); GetString() on it used to throw
                    reader.Read();
                    using var doc = JsonDocument.ParseValue(ref reader);
                    error = doc.RootElement.GetRawText();
                }
                else
                {
                    reader.Skip();
                }
            }

            if (error == null && base64Encoded && length > 0)
            {
                var buffer = (rented ?? _scratchBuffer).AsSpan(0, length);
                // A failed decode used to be dropped silently, producing a truncated/corrupt PDF
                if (Base64.DecodeFromUtf8InPlace(buffer, out length) != OperationStatus.Done)
                    throw new InvalidDataException("Chromium returned PDF data that is not valid base64.");
            }

            return (length, eof, error);
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
    }
}