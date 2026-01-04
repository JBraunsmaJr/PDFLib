using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace PDFLib.Console.RPC;


public class CdpDispatcher
{
    private readonly Stream _writer;
    private readonly Stream _reader;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private int _nextId = 0;

    public CdpDispatcher(Stream writer, Stream reader)
    {
        _writer = writer;
        _reader = reader;
        Task.Run(ReadLoop);
    }

    public async Task<JsonElement> SendCommandAsync(
        string method, 
        object? @params = null, 
        string? sessionId = null, 
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var request = new Dictionary<string, object>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params ?? new { }
        };

        if (!string.IsNullOrEmpty(sessionId))
        {
            request["sessionId"] = sessionId;
        }

        var json = JsonSerializer.Serialize(request);
        var buffer = Encoding.UTF8.GetBytes(json + "\0"); // Protocol requires null terminator

        await _writer.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await _writer.FlushAsync(cancellationToken);

        return await tcs.Task;
    }

    private async Task ReadLoop()
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];

        try
        {
            while (true)
            {
                var bytesRead = await _reader.ReadAsync(chunk, 0, chunk.Length);
                if (bytesRead == 0) break;

                for (var i = 0; i < bytesRead; i++)
                {
                    if (chunk[i] == 0) // Message boundary
                    {
                        var rawJson = Encoding.UTF8.GetString(buffer.ToArray());
                        buffer.Clear();
                        ProcessMessage(rawJson);
                    }
                    else
                    {
                        buffer.Add(chunk[i]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            foreach (var tcs in _pendingRequests.Values) tcs.TrySetException(ex);
        }
    }

    private void ProcessMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("id", out var idProp) ||
            !_pendingRequests.TryRemove(idProp.GetInt32(), out var tcs)) return;
        if (root.TryGetProperty("error", out var error))
            tcs.SetException(new Exception($"CDP Error: {error.GetRawText()}"));
        else
            tcs.SetResult(root.GetProperty("result").Clone());
    }
}