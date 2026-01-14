using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;

namespace PDFLib.Chromium.RPC;

/// <summary>
/// Chrome DevTools Protocol (CDP) dispatcher for handling communication with a Chrome browser instance.
/// </summary>
public class CdpDispatcher
{
    private readonly ConcurrentDictionary<string, List<Action<JsonElement>>> _eventHandlers = new();
    private readonly List<string> _fastCacheList = new();
    private readonly ConcurrentDictionary<int, IResponseHandler> _pendingRequests = new();
    private readonly PipeReader _pipeReader;
    private readonly ConcurrentDictionary<string, string> _stringCache = new(StringComparer.Ordinal);
    private readonly ArrayBufferWriter<byte> _writeBuffer = new(4096);
    private readonly Stream _writer;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private int _nextId;

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

    #endregion
    
    /// <summary>
    /// Initializes a new instance of the <see cref="CdpDispatcher"/> class.
    /// </summary>
    /// <param name="writer">The stream to write commands to.</param>
    /// <param name="reader">The stream to read responses from.</param>
    public CdpDispatcher(Stream writer, Stream reader)
    {
        _writer = writer;
        _pipeReader = PipeReader.Create(reader);
        Task.Run(ReadLoop);
    }

    /// <summary>
    /// Registers an event handler for a specific CDP method.
    /// </summary>
    /// <param name="method">Method to handle from CDP</param>
    /// <param name="handler">Handler for the specified CDP method</param>
    public void On(string method, Action<JsonElement> handler)
    {
        // Add to fast cache list for iteration
        if (_stringCache.TryAdd(method, method))
            lock (_fastCacheList)
            {
                _fastCacheList.Add(method);
            }

        _eventHandlers.AddOrUpdate(method, _ => [handler], (_, list) =>
        {
            lock (list)
            {
                list.Add(handler);
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
            list.Remove(handler);
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
    public async Task SendCommandInternalAsync(string method, object? @params, string? sessionId,
        IResponseHandler handler, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        _pendingRequests[id] = handler;

        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            _writeBuffer.Clear();

            // ReSharper disable once UseAwaitUsing
            // Await using would introduce context-switching and extra memory for async structure
            using (var writer = new Utf8JsonWriter(_writeBuffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber(IdEncoded, id);
                writer.WriteString(MethodEncoded, method);
                writer.WritePropertyName(ParamsEncoded);
                if (@params != null) JsonSerializer.Serialize(writer, @params, @params.GetType());
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
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Sends a command to the CDP
    /// </summary>
    /// <param name="method">Method to invoke/pass data to</param>
    /// <param name="params">Parameters to pass into CDP</param>
    /// <param name="sessionId">SessionID is how we identify where to send the data to</param>
    /// <param name="cancellationToken"></param>
    /// <returns>JsonElement that CDP returns</returns>
    public async Task<JsonElement> SendCommandAsync(string method, object? @params = null, string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var handler = new JsonResponseHandler();
        await SendCommandInternalAsync(method, @params, sessionId, handler, cancellationToken);
        return await handler.Task;
    }

    private async Task ReadLoop()
    {
        try
        {
            while (true)
            {
                var result = await _pipeReader.ReadAsync();
                var buffer = result.Buffer;
                while (TryReadMessage(ref buffer, out var message)) ProcessMessage(message);
                _pipeReader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex)
        {
            foreach (var handler in _pendingRequests.Values) handler.SetException(ex);
        }
        finally
        {
            await _pipeReader.CompleteAsync();
        }
    }

    private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message)
    {
        var position = buffer.PositionOf((byte)0);
        if (position == null)
        {
            message = default;
            return false;
        }

        message = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private void ProcessMessage(ReadOnlySequence<byte> message)
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
                if (reader.TokenType == JsonTokenType.Number) id = reader.GetInt32();
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

        if (id.HasValue && _pendingRequests.TryRemove(id.Value, out var handler)) handler.Handle(message);

        if (method == null || !_eventHandlers.TryGetValue(method, out var handlers)) return;

        var eventReader = new Utf8JsonReader(message);
        JsonElement paramsElement = default;

        // Only parse params if strictly necessary (expensive)
        while (eventReader.Read())
        {
            if (eventReader.TokenType == JsonTokenType.PropertyName && eventReader.ValueTextEquals("params"u8))
            {
                eventReader.Read();
                paramsElement = JsonElement.ParseValue(ref eventReader);
                break;
            }

            if (eventReader.TokenType == JsonTokenType.PropertyName) eventReader.Skip();
        }

        lock (handlers)
        {
            foreach (var h in handlers) h(paramsElement);
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
        void Handle(ReadOnlySequence<byte> message);

        /// <summary>
        /// Sets an exception if the command failed.
        /// </summary>
        /// <param name="ex">The exception.</param>
        void SetException(Exception ex);
    }

    private class JsonResponseHandler : IResponseHandler
    {
        private readonly TaskCompletionSource<JsonElement> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonElement> Task => _tcs.Task;

        public void Handle(ReadOnlySequence<byte> message)
        {
            try
            {
                // Optimization: Scan for result/error before parsing full document
                var reader = new Utf8JsonReader(message);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

                    if (reader.ValueTextEquals("result"u8))
                    {
                        reader.Read();
                        using var doc = JsonDocument.ParseValue(ref reader);
                        _tcs.TrySetResult(doc.RootElement.Clone());
                        return;
                    }

                    if (reader.ValueTextEquals("error"u8))
                    {
                        reader.Read();
                        using var doc = JsonDocument.ParseValue(ref reader);
                        _tcs.TrySetException(new Exception($"CDP Error: {doc.RootElement.GetRawText()}"));
                        return;
                    }

                    reader.Skip();
                }

                _tcs.TrySetResult(default);
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
    }
}