namespace PDFLib.Chromium.Tests;

public class CdpDispatcherTests
{
    [Fact]
    public async Task ConcurrentCommands_EachGetTheirOwnResponse_EvenWhenRepliesAreOutOfOrder()
    {
        // Earlier commands answer later, so replies come back in reverse order
        await using var server = new FakeCdpServer(async (id, method, _) =>
        {
            await Task.Delay(Math.Max(0, 200 - id * 10));
            return $"{{\"echo\":\"{method}\"}}";
        });

        var calls = Enumerable.Range(0, 16).Select(async i =>
        {
            using var doc = await server.Dispatcher.SendCommandAsync($"Test.method{i}");
            return (i, echoed: doc!.RootElement.GetProperty("echo").GetString());
        });

        var results = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));

        foreach (var (i, echoed) in results)
            Assert.Equal($"Test.method{i}", echoed);
    }

    [Fact]
    public async Task PendingCommands_Fail_WhenTheBrowserClosesThePipe()
    {
        await using var server = new FakeCdpServer((_, _, _) => Task.FromResult<string?>(null)); // never answers

        var pending = server.Dispatcher.SendCommandAsync("Never.answered");
        await Task.Delay(100);
        server.Close();

        await Assert.ThrowsAnyAsync<Exception>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        // ...and nothing hangs afterwards either
        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await server.Dispatcher.SendCommandAsync("After.close").WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Cancellation_StopsWaitingForAResponse()
    {
        await using var server = new FakeCdpServer((_, _, _) => Task.FromResult<string?>(null));
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await server.Dispatcher.SendCommandAsync("Slow", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ThrowingEventHandler_DoesNotBreakTheReadLoop()
    {
        await using var server = new FakeCdpServer();
        server.Dispatcher.On("Anything", _ => throw new InvalidOperationException("subscriber bug"));

        using var doc = await server.Dispatcher.SendCommandAsync("Still.works").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Still.works", doc!.RootElement.GetProperty("echo").GetString());
    }

    [Fact]
    public async Task SessionScopedHandlers_OnlySeeEventsFromTheirOwnSession()
    {
        await using var server = new FakeCdpServer();
        var pageA = new List<string>();
        var pageB = new List<string>();
        var everyone = new List<string>();
        server.Dispatcher.On("Network.requestWillBeSent", "A", p => pageA.Add(p.GetProperty("requestId").GetString()!));
        server.Dispatcher.On("Network.requestWillBeSent", "B", p => pageB.Add(p.GetProperty("requestId").GetString()!));
        server.Dispatcher.On("Network.requestWillBeSent", p => everyone.Add(p.GetProperty("requestId").GetString()!));

        server.Emit("""{"method":"Network.requestWillBeSent","params":{"requestId":"1"},"sessionId":"A"}""");
        server.Emit("""{"sessionId":"B","method":"Network.requestWillBeSent","params":{"requestId":"2"}}""");

        // Events are processed in order on the read loop, so a round trip flushes them
        using var _ = await server.Dispatcher.SendCommandAsync("Sync").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["1"], pageA);
        Assert.Equal(["2"], pageB);
        Assert.Equal(["1", "2"], everyone);
    }

    [Fact]
    public async Task Params_AreSentWithoutHtmlEscaping_AndRoundTripIntact()
    {
        const string html = "<p class=\"x\">Tom & Jerry's caf\u00e9 \u4f60\u597d</p>";
        string? received = null;
        await using var server = new FakeCdpServer((_, _, p) =>
        {
            received = p.GetProperty("html").GetString();
            return Task.FromResult<string?>("{}");
        });

        using var _ = await server.Dispatcher.SendCommandAsync("Page.setDocumentContent", new { html })
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(html, received);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32 * 1024)]        // exactly one segment
    [InlineData(32 * 1024 + 1)]
    [InlineData(3_000_000)]
    public async Task LargeStringCommand_ArrivesIntact(int length)
    {
        // Mix of markup, quotes, control chars, non-ASCII and surrogate pairs (which may straddle segment boundaries)
        const string alphabet = "<div class=\"a\">&'\n\t\\é你😀</div>";
        var sb = new System.Text.StringBuilder(length + 16);
        while (sb.Length < length) sb.Append(alphabet);
        var html = sb.ToString(0, length);
        if (length > 0 && char.IsHighSurrogate(html[^1])) html = html[..^1] + "x";

        string? receivedHtml = null, receivedFrame = null;
        await using var server = new FakeCdpServer((_, method, p) =>
        {
            if (method == "Page.setDocumentContent")
            {
                receivedFrame = p.GetProperty("frameId").GetString();
                receivedHtml = p.GetProperty("html").GetString();
            }

            return Task.FromResult<string?>("{}");
        });

        using var first = await server.Dispatcher
            .SendCommandWithLargeStringAsync("Page.setDocumentContent", "S1", "frameId", "F1", "html", html)
            .WaitAsync(TimeSpan.FromSeconds(20));
        // The connection must still be usable for normal commands afterwards
        using var second = await server.Dispatcher.SendCommandAsync("Page.enable").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("F1", receivedFrame);
        Assert.Equal(html, receivedHtml);
    }
}
