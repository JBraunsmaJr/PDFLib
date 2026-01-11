using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;

namespace PDFLib.Console.RPC;

/// <summary>
/// Chrome DevTools Protocol (CDP) dispatcher for handling communication with a Chrome browser instance.
/// </summary>
public class CdpDispatcher
{
    private readonly Stream _writer;
    private readonly PipeReader _pipeReader;
    private readonly ConcurrentDictionary<int, IResponseHandler> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, List<Action<JsonElement>>> _eventHandlers = new();
    private readonly ConcurrentDictionary<string, string> _stringCache = new();
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
    private readonly ArrayBufferWriter<byte> _writeBuffer = new(4096);
    private int _nextId;
    
    #region PreAllocated
    /*
     * Preallocated variables of commonly used things
     * to help reduce allocations
     */
    
    private static readonly byte[] IdBytes = "id"u8.ToArray();
    private static readonly byte[] MethodBytes = "method"u8.ToArray();
    private static readonly byte[] ResultBytes = "result"u8.ToArray();
    private static readonly byte[] ErrorBytes = "error"u8.ToArray();
    private static readonly byte[] ParamsBytes = "params"u8.ToArray();
    private static readonly byte[] SessionIdBytes = "sessionId"u8.ToArray();
    private static readonly byte[] EmptyParamsBytes = "{}"u8.ToArray();

    private static readonly JsonEncodedText IdEncoded = JsonEncodedText.Encode("id"u8);
    private static readonly JsonEncodedText MethodEncoded = JsonEncodedText.Encode("method"u8);
    private static readonly JsonEncodedText ParamsEncoded = JsonEncodedText.Encode("params"u8);
    private static readonly JsonEncodedText SessionIdEncoded = JsonEncodedText.Encode("sessionId"u8);
    #endregion
    
    public interface IResponseHandler
    {
        void Handle(ReadOnlySequence<byte> message);
        void SetException(Exception ex);
    }

    private class JsonResponseHandler : IResponseHandler
    {
        private readonly TaskCompletionSource<JsonElement> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonElement> Task => _tcs.Task;

        public void Handle(ReadOnlySequence<byte> message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var result))
                {
                    _tcs.SetResult(result.ValueKind == JsonValueKind.Undefined ? default : result.Clone());
                }
                else if (root.TryGetProperty("error", out var error))
                {
                    _tcs.SetException(new Exception($"CDP Error: {error.GetRawText()}"));
                }
                else
                {
                    _tcs.SetResult(default);
                }
            }
            catch (Exception ex)
            {
                _tcs.SetException(ex);
            }
        }

        public void SetException(Exception ex) => _tcs.TrySetException(ex);
    }

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
    /// Sends a command to the CDP
    /// </summary>
    /// <param name="method">Method to invoke/pass data to</param>
    /// <param name="params">Parameters to pass into CDP</param>
    /// <param name="sessionId">SessionID is how we identify where to send the data to</param>
    /// <param name="cancellationToken"></param>
    /// <returns>JsonElement that CDP returns</returns>
    public async Task<JsonElement> SendCommandAsync(
        string method, 
        object? @params = null, 
        string? sessionId = null, 
        CancellationToken cancellationToken = default)
    {
        var handler = new JsonResponseHandler();
        await SendCommandInternalAsync(method, @params, sessionId, handler, cancellationToken);
        return await handler.Task;
    }

    public async Task SendCommandInternalAsync(
        string method, 
        object? @params, 
        string? sessionId, 
        IResponseHandler handler,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        _pendingRequests[id] = handler;

        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            _writeBuffer.Clear();
            await using (var writer = new Utf8JsonWriter(_writeBuffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber(IdEncoded, id);
                writer.WriteString(MethodEncoded, method);

                writer.WritePropertyName(ParamsEncoded);
                
                if (@params != null)
                {
                    JsonSerializer.Serialize(writer, @params, @params.GetType());
                }
                else
                {
                    writer.WriteRawValue(EmptyParamsBytes);
                }

                if (!string.IsNullOrEmpty(sessionId))
                {
                    writer.WriteString(SessionIdEncoded, sessionId);
                }

                writer.WriteEndObject();
            }

            // Add null terminator
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

    private async Task ReadLoop()
    {
        try
        {
            while (true)
            {
                var result = await _pipeReader.ReadAsync();
                var buffer = result.Buffer;

                while (TryReadMessage(ref buffer, out var message))
                {
                    ProcessMessage(message);
                }

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

    private string? GetCachedString(ref Utf8JsonReader reader)
    {
        // For short strings, we can avoid string allocation by checking the cache.
        // We only do this if it's not a ValueSequence for simplicity.
        if (reader.HasValueSequence) return reader.GetString();

        foreach (var entry in _stringCache)
        {
            if (reader.ValueTextEquals(entry.Key))
            {
                return entry.Value;
            }
        }

        var s = reader.GetString();
        if (s is { Length: < 128 }) _stringCache.TryAdd(s, s);
        return s;
    }
    

    private void ProcessMessage(ReadOnlySequence<byte> message)
    {
        // Use Utf8JsonReader for initial pass to avoid JsonDocument overhead if possible
        var reader = new Utf8JsonReader(message);
        int? id = null;
        string? method = null;
        var hasResult = false;
        var hasError = false;

        // Quickly find id and method
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (!reader.HasValueSequence)
                {
                    ProcessProperty(reader.ValueSpan, ref reader, ref id, ref method, ref hasResult, ref hasError);
                }
                else
                {
                    // Most CDP property names are short and fit in a small buffer.
                    // We can use a pooled array for larger ones if needed, but 128 is plenty for CDP.
                    var rented = ArrayPool<byte>.Shared.Rent((int)reader.ValueSequence.Length);
                    try
                    {
                        reader.ValueSequence.CopyTo(rented);
                        ProcessProperty(rented.AsSpan(0, (int)reader.ValueSequence.Length), ref reader, ref id, ref method, ref hasResult, ref hasError);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
            }
            if (id.HasValue && (method != null || hasResult || hasError)) break;
        }

        if (method != null)
        {
            if (_eventHandlers.TryGetValue(method, out var handlers))
            {
                // Only parse params if we actually have handlers
                // Use Utf8JsonReader to find the params property instead of JsonDocument.Parse
                var paramsReader = new Utf8JsonReader(message);
                var foundParams = false;
                while (paramsReader.Read())
                {
                    if (paramsReader.TokenType != JsonTokenType.PropertyName) continue;
                    bool isParams;
                    if (!paramsReader.HasValueSequence)
                    {
                        isParams = paramsReader.ValueSpan.SequenceEqual(ParamsBytes);
                    }
                    else
                    {
                        var pRented = ArrayPool<byte>.Shared.Rent((int)paramsReader.ValueSequence.Length);
                        paramsReader.ValueSequence.CopyTo(pRented);
                        isParams = pRented.AsSpan(0, (int)paramsReader.ValueSequence.Length).SequenceEqual(ParamsBytes);
                        ArrayPool<byte>.Shared.Return(pRented);
                    }
                    paramsReader.Read();

                    if (isParams)
                    {
                        var p = JsonElement.ParseValue(ref paramsReader);
                        lock (handlers)
                        {
                            foreach (var h in handlers)
                            {
                                h(p);
                            }
                        }
                        foundParams = true;
                        break;
                    }
                    paramsReader.Skip();
                }

                if (!foundParams)
                {
                    lock (handlers)
                    {
                        foreach (var h in handlers)
                        {
                            h(default);
                        }
                    }
                }
            }
        }

        if (id.HasValue && _pendingRequests.TryRemove(id.Value, out var handler))
        {
            handler.Handle(message);
        }
    }

    private void ProcessProperty(ReadOnlySpan<byte> span, ref Utf8JsonReader reader, ref int? id, ref string? method, ref bool hasResult, ref bool hasError)
    {
        reader.Read();
        if (span.SequenceEqual(IdBytes))
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                id = reader.GetInt32();
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                if (reader.HasValueSequence)
                {
                    var idStr = reader.GetString();
                    if (idStr != null) id = int.Parse(idStr);
                }
                else
                {
                    if (Utf8Parser.TryParse(reader.ValueSpan, out int val, out _))
                    {
                        id = val;
                    }
                }
            }
        }
        else if (span.SequenceEqual(MethodBytes))
        {
            method = GetCachedString(ref reader);
        }
        else if (span.SequenceEqual(ParamsBytes))
        {
            // If we have handlers, we'll need to parse this later
            // but we need to move the reader forward
            reader.Skip();
        }
        else if (span.SequenceEqual(SessionIdBytes))
        {
            if (reader.HasValueSequence)
            {
                reader.Skip();
            }
            else
            {
                // Session IDs are also frequently reused
                GetCachedString(ref reader);
            }
        }
        else if (span.SequenceEqual(ResultBytes))
        {
            hasResult = true;
            reader.Skip();
        }
        else if (span.SequenceEqual(ErrorBytes))
        {
            hasError = true;
            reader.Skip();
        }
        else
        {
            reader.Skip();
        }
    }
}