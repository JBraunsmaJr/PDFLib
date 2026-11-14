using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PDFLib.Chromium.RPC;

/// <summary>
/// Chrome DevTools Protocol (CDP) dispatcher for handling communication with a Chrome browser instance.
/// </summary>
public class CdpDispatcher
{
    private readonly ConcurrentDictionary<string, List<EventSubscription>> _eventHandlers = new();
    private readonly List<string> _fastCacheList = new();
    private readonly ConcurrentDictionary<int, IResponseHandler> _pendingRequests = new();
    private readonly PipeReader _pipeReader;
    private readonly ConcurrentDictionary<string, string> _stringCache = new(StringComparer.Ordinal);
    private ArrayBufferWriter<byte> _writeBuffer = new(4096);
    private readonly Stream _writer;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private int _nextId;
    private volatile Exception? _fault;

    #region PreAllocated

    /*
     * Preallocated variables of commonly used things to help
     * reduce allocations
     */
    private static readonly JsonEncodedText IdEncoded = JsonEncodedText.Encode("id"u8);
    private static readonly JsonEncodedText MethodEncoded = JsonEncodedText.Encode("method"u8);
    private static readonly JsonEncodedText ParamsEncoded = JsonEncodedText.Encode("params"u8);
    private static readonly JsonEncodedText SessionIdEncoded = JsonEncodedText.Encode("sessionId"u8);
    private static readonly byte[] EmptyParamsBytes = "{}"u8.ToArray();

    /*
     * The default encoder escapes <, >, &, ', " and all non-ASCII as \uXXXX (6 bytes each). HTML is mostly
     * made of those characters, so Page.setDocumentContent payloads were several times larger than needed.
     * This is JSON over a private pipe, never embedded in markup, so the relaxed encoder is safe here.
     */
    private static readonly JsonSerializerOptions ParamsSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Don't hold on to a huge write buffer forever just because one document was huge
    private const int MaxRetainedWriteBuffer = 1024 * 1024;

    private readonly record struct EventSubscription(string? SessionId, Action<JsonElement> Handler);

    private readonly record struct LargeStringParams(string SmallName, string? SmallValue, string LargeName, string LargeValue);

    private const int LargeStringSegmentChars = 32 * 1024;
    private const int LargeStringDrainThreshold = 64 * 1024;

    #endregion
    
    /// <summary>
    /// Initializes a new instance of the <see cref="CdpDispatcher"/> class.
    /// </summary>
    /// <param name="writer">The stream to write commands to.</param>
    /// <param name="reader">The stream to read responses from.</param>
    public CdpDispatcher(Stream writer, Stream reader)
    {
        _writer = writer;
        /*
         * PDF data arrives as ~350KB messages. With the default 4KB buffer each message took ~85 reads
         * and produced an 85-segment sequence; 64KB matches the default Linux pipe capacity.
         */
        _pipeReader = PipeReader.Create(reader, new StreamPipeReaderOptions(bufferSize: 64 * 1024, minimumReadSize: 16 * 1024));
        Task.Run(ReadLoop);
    }

    /// <summary>
    /// Registers an event handler for a specific CDP method, for events from every session.
    /// </summary>
    /// <param name="method">Method to handle from CDP</param>
    /// <param name="handler">Handler for the specified CDP method</param>
    public void On(string method, Action<JsonElement> handler) => On(method, null, handler);

    /// <summary>
    /// Registers an event handler for a specific CDP method, limited to events from one session (page).
    /// </summary>
    /// <param name="method">Method to handle from CDP</param>
    /// <param name="sessionId">Only events from this session are delivered. Null delivers events from all sessions.</param>
    /// <param name="handler">Handler for the specified CDP method</param>
    public void On(string method, string? sessionId, Action<JsonElement> handler)
    {
        // Add to fast cache list for iteration
        if (_stringCache.TryAdd(method, method))
            lock (_fastCacheList)
            {
                _fastCacheList.Add(method);
            }

        var subscription = new EventSubscription(sessionId, handler);
        _eventHandlers.AddOrUpdate(method, _ => [subscription], (_, list) =>
        {
            lock (list)
            {
                list.Add(subscription);
            }

            return list;
        });
    }

    /// <summary>
    /// Removes handler from CDP
    /// </summary>
    /// <param name="method"></param>
    /// <param name="handler"></param>
    public void Off(string method, Action<JsonElement> handler)
    {
        if (!_eventHandlers.TryGetValue(method, out var list)) return;
        lock (list)
        {
            var index = list.FindIndex(s => s.Handler == handler);
            if (index >= 0) list.RemoveAt(index);
        }
    }

    /// <summary>
    /// Sends a CDP command and uses a custom response handler.
    /// </summary>
    /// <param name="method">The CDP method name.</param>
    /// <param name="params">The command parameters.</param>
    /// <param name="sessionId">The optional session ID.</param>
    /// <param name="handler">The handler to process the response.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SendCommandInternalAsync(string method, object? @params, string? sessionId,
        IResponseHandler handler, CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(method, @params, sessionId, handler, null, cancellationToken);
    }

    private async Task<int> SendCoreAsync(string method, object? @params, string? sessionId,
        IResponseHandler handler, LargeStringParams? large, CancellationToken cancellationToken)
    {
        if (_fault is { } fault) throw new IOException("The CDP connection is closed.", fault);

        var id = Interlocked.Increment(ref _nextId);
        _pendingRequests[id] = handler;

        // The read loop may have died between the check above and registering the handler
        if (_fault is { } lateFault)
        {
            _pendingRequests.TryRemove(id, out _);
            throw new IOException("The CDP connection is closed.", lateFault);
        }

        try
        {
            await _writeSemaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }

        try
        {
            // ResetWrittenCount rather than Clear: Clear zeroes the whole buffer, which is wasted work
            // for every command sent after a large document.
            _writeBuffer.ResetWrittenCount();

            // ReSharper disable once UseAwaitUsing
            // Await using would introduce context-switching and extra memory for async structure
            using (var writer = new Utf8JsonWriter(_writeBuffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber(IdEncoded, id);
                writer.WriteString(MethodEncoded, method);
                writer.WritePropertyName(ParamsEncoded);
                if (large is { } l)
                {
                    /*
                     * Utf8JsonWriter reserves worst-case space (several bytes per char) for a string before
                     * writing it, so sending a 30MB document in one go allocated hundreds of MB. Writing it in
                     * segments and draining the buffer into the pipe as we go keeps this at a few hundred KB.
                     */
                    writer.WriteStartObject();
                    writer.WriteString(l.SmallName, l.SmallValue);
                    writer.WritePropertyName(l.LargeName);

                    var remaining = l.LargeValue.AsMemory();
                    do
                    {
                        var take = Math.Min(LargeStringSegmentChars, remaining.Length);
                        writer.WriteStringValueSegment(remaining.Span[..take], take == remaining.Length);
                        remaining = remaining[take..];
                        await writer.FlushAsync(cancellationToken);

                        if (_writeBuffer.WrittenCount >= LargeStringDrainThreshold)
                        {
                            // Not cancellable: stopping half way through a message would corrupt the protocol stream
                            await _writer.WriteAsync(_writeBuffer.WrittenMemory, CancellationToken.None);
                            _writeBuffer.ResetWrittenCount();
                        }
                    } while (remaining.Length > 0);

                    writer.WriteEndObject();
                }
                else if (@params != null) JsonSerializer.Serialize(writer, @params, @params.GetType(), ParamsSerializerOptions);
                else writer.WriteRawValue(EmptyParamsBytes);
                if (!string.IsNullOrEmpty(sessionId)) writer.WriteString(SessionIdEncoded, sessionId);
                writer.WriteEndObject();
            }

            var span = _writeBuffer.GetSpan(1);
            span[0] = 0;
            _writeBuffer.Advance(1);
            await _writer.WriteAsync(_writeBuffer.WrittenMemory, cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }
        finally
        {
            if (_writeBuffer.Capacity > MaxRetainedWriteBuffer) _writeBuffer = new ArrayBufferWriter<byte>(4096);
            _writeSemaphore.Release();
        }

        return id;
    }

    /// <summary>
    /// Sends a command to the CDP
    /// </summary>
    /// <param name="method">Method to invoke/pass data to</param>
    /// <param name="params">Parameters to pass into CDP</param>
    /// <param name="sessionId">SessionID is how we identify where to send the data to</param>
    /// <param name="cancellationToken"></param>
    /// <returns>JsonDocument that CDP returns. Caller MUST dispose it.</returns>
    public Task<JsonDocument?> SendCommandAsync(string method, object? @params = null, string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(method, @params, sessionId, null, cancellationToken);
    }

    /// <summary>
    /// Sends a command whose params are one small string plus one potentially huge string (e.g. a whole
    /// HTML document), streaming the large value to the browser instead of buffering the entire command.
    /// </summary>
    internal Task<JsonDocument?> SendCommandWithLargeStringAsync(string method, string? sessionId,
        string smallName, string? smallValue, string largeName, string largeValue,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(method, null, sessionId,
            new LargeStringParams(smallName, smallValue, largeName, largeValue), cancellationToken);
    }

    private async Task<JsonDocument?> SendCommandAsync(string method, object? @params, string? sessionId,
        LargeStringParams? large, CancellationToken cancellationToken)
    {
        /*
         * One handler per command. Sharing a single handler (and its TaskCompletionSource) between
         * commands breaks as soon as two commands are in flight at once, e.g. concurrent renders.
         */
        var handler = new JsonResponseHandler();
        var id = await SendCoreAsync(method, @params, sessionId, handler, large, cancellationToken);
        try
        {
            return await handler.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!handler.Task.IsCompleted)
        {
            // Stop tracking the command, and make sure a reply that still shows up gets disposed
            _pendingRequests.TryRemove(id, out _);
            _ = handler.Task.ContinueWith(static t =>
            {
                if (t.IsCompletedSuccessfully) t.Result?.Dispose();
                else _ = t.Exception;
            }, TaskScheduler.Default);
            throw;
        }
    }

    private async Task ReadLoop()
    {
        Exception? failure = null;
        try
        {
            // Bytes at the start of the buffer already known to contain no terminator. Without this,
            // every read re-scanned the whole partial message from the beginning (quadratic for big messages).
            long scanned = 0;

            while (true)
            {
                var result = await _pipeReader.ReadAsync();
                var buffer = result.Buffer;
                while (TryReadMessage(ref buffer, ref scanned, out var message)) await ProcessMessageAsync(message);
                _pipeReader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            // Whatever the reason we stopped reading (browser exited, pipe closed, bug), nobody is
            // going to answer the outstanding commands. Fail them instead of letting callers hang.
            _fault = failure ?? new EndOfStreamException("The browser closed the CDP pipe.");
            foreach (var id in _pendingRequests.Keys)
                if (_pendingRequests.TryRemove(id, out var handler))
                    handler.SetException(_fault);

            await _pipeReader.CompleteAsync();
        }
    }

    private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, ref long scanned,
        out ReadOnlySequence<byte> message)
    {
        var position = buffer.Slice(scanned).PositionOf((byte)0);
        if (position == null)
        {
            scanned = buffer.Length;
            message = default;
            return false;
        }

        message = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        scanned = 0;
        return true;
    }

    private async ValueTask ProcessMessageAsync(ReadOnlySequence<byte> message)
    {
        var reader = new Utf8JsonReader(message);
        int? id = null;
        string? method = null;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            if (reader.ValueTextEquals("id"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.Number)
                {
                    id = reader.GetInt32();
                    // It's a command response; the handler parses the rest. Don't tokenize a (possibly
                    // several hundred KB) payload twice.
                    break;
                }
            }
            else if (reader.ValueTextEquals("method"u8))
            {
                reader.Read();

                // Check cache WITHOUT allocating string first
                method = GetCachedStringZeroAlloc(ref reader);
            }
            else if (reader.ValueTextEquals("sessionId"u8))
            {
                reader.Skip();
            }
            else
            {
                reader.Skip();
            }
        }

        if (id.HasValue && _pendingRequests.TryRemove(id.Value, out var handler))
        {
            await handler.Handle(message);
            return;
        }

        if (method == null || !_eventHandlers.TryGetValue(method, out var handlers)) return;
        if (handlers.Count == 0) return;

        var eventReader = new Utf8JsonReader(message);
        JsonElement paramsElement = default;
        string? eventSessionId = null;

        // Only parse params if strictly necessary (expensive)
        while (eventReader.Read())
        {
            if (eventReader.TokenType != JsonTokenType.PropertyName) continue;

            if (eventReader.ValueTextEquals("params"u8))
            {
                eventReader.Read();
                paramsElement = JsonElement.ParseValue(ref eventReader);
            }
            else if (eventReader.ValueTextEquals("sessionId"u8))
            {
                eventReader.Read();
                eventSessionId = eventReader.GetString();
            }
            else
            {
                eventReader.Skip();
            }
        }

        // Use a local copy of the handlers list to minimize lock duration
        EventSubscription[]? subscriptions = null;
        lock (handlers)
        {
            if (handlers.Count > 0)
                subscriptions = handlers.ToArray();
        }

        if (subscriptions != null)
        {
            foreach (var subscription in subscriptions)
            {
                // Events are multiplexed over one pipe; a page must not see another page's events
                if (subscription.SessionId != null && subscription.SessionId != eventSessionId) continue;

                try
                {
                    subscription.Handler(paramsElement);
                }
                catch
                {
                    // A faulty subscriber must not take down the read loop (and every pending command with it)
                }
            }
        }
    }

    private string? GetCachedStringZeroAlloc(ref Utf8JsonReader reader)
    {
        if (reader.HasValueSequence) return reader.GetString();

        /*
         * Check fast cache list (Iteration vs Allocation)
         * Since we typically only have 10-20 event types we listen to, this loop is
         * cheaper than a string alloc
         * We can't iterate the ConcurrentDictionary keys without alloc
         */
        lock (_fastCacheList)
        {
            foreach (var cached in _fastCacheList)
                if (reader.ValueTextEquals(cached))
                    return cached;
        }

        // Fallback (Rare for protocols)
        return reader.GetString();
    }

    /// <summary>
    /// Defines a handler for CDP responses.
    /// </summary>
    public interface IResponseHandler
    {
        /// <summary>
        /// Handles the incoming message.
        /// </summary>
        /// <param name="message">The raw message bytes.</param>
        ValueTask Handle(ReadOnlySequence<byte> message);

        /// <summary>
        /// Sets an exception if the command failed.
        /// </summary>
        /// <param name="ex">The exception.</param>
        void SetException(Exception ex);
    }

    private class JsonResponseHandler : IResponseHandler
    {
        private readonly TaskCompletionSource<JsonDocument?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonDocument?> Task => _tcs.Task;

        public ValueTask Handle(ReadOnlySequence<byte> message)
        {
            try
            {
                var reader = new Utf8JsonReader(message);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

                    if (reader.ValueTextEquals("result"u8))
                    {
                        reader.Read();
                        _tcs.TrySetResult(JsonDocument.ParseValue(ref reader));
                        return ValueTask.CompletedTask;
                    }

                    if (reader.ValueTextEquals("error"u8))
                    {
                        reader.Read();
                        using var doc = JsonDocument.ParseValue(ref reader);
                        _tcs.TrySetException(new Exception($"CDP Error: {doc.RootElement.GetRawText()}"));
                        return ValueTask.CompletedTask;
                    }

                    reader.Skip();
                }

                _tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }

            return ValueTask.CompletedTask;
        }

        public void SetException(Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }
}