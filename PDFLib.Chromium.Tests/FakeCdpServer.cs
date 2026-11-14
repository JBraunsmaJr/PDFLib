using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using PDFLib.Chromium.RPC;

namespace PDFLib.Chromium.Tests;

/// <summary>
/// Minimal in-memory stand-in for Chromium's --remote-debugging-pipe: NUL-terminated JSON in both directions.
/// </summary>
internal sealed class FakeCdpServer : IAsyncDisposable
{
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private readonly Task _loop;

    /// <param name="respond">
    /// Maps (id, method, params) to the JSON of the "result" object, or null to never answer.
    /// When omitted every command is answered immediately with {"echo": method}.
    /// </param>
    public FakeCdpServer(Func<int, string, JsonElement, Task<string?>>? respond = null)
    {
        Dispatcher = new CdpDispatcher(_toServer.Writer.AsStream(), _toClient.Reader.AsStream());
        _loop = Task.Run(() => Serve(respond));
    }

    public CdpDispatcher Dispatcher { get; }

    /// <summary>Simulates the browser going away.</summary>
    public void Close() => _toClient.Writer.Complete();

    /// <summary>Sends a raw message (e.g. an event) to the client.</summary>
    public void Emit(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\0");
        lock (_toClient.Writer)
        {
            _toClient.Writer.WriteAsync(bytes).AsTask().GetAwaiter().GetResult();
        }
    }

    private async Task Serve(Func<int, string, JsonElement, Task<string?>>? respond)
    {
        var pending = new List<byte>();
        while (true)
        {
            var result = await _toServer.Reader.ReadAsync();
            foreach (var segment in result.Buffer)
                pending.AddRange(segment.Span.ToArray());
            _toServer.Reader.AdvanceTo(result.Buffer.End);

            int nul;
            while ((nul = pending.IndexOf(0)) >= 0)
            {
                var json = pending.GetRange(0, nul).ToArray();
                pending.RemoveRange(0, nul + 1);
                using var doc = JsonDocument.Parse(json);
                var id = doc.RootElement.GetProperty("id").GetInt32();
                var method = doc.RootElement.GetProperty("method").GetString()!;
                var parameters = doc.RootElement.GetProperty("params").Clone();

                // Each command is answered on its own task so replies can overtake each other
                _ = Task.Run(async () =>
                {
                    var result = respond == null ? $"{{\"echo\":\"{method}\"}}" : await respond(id, method, parameters);
                    if (result == null) return;
                    Emit($"{{\"id\":{id},\"result\":{result}}}");
                });
            }

            if (result.IsCompleted) return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _toServer.Writer.CompleteAsync();
        await _toClient.Writer.CompleteAsync();
        await Task.WhenAny(_loop, Task.Delay(500));
    }
}
