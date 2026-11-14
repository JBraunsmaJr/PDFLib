using System.Diagnostics;

namespace PDFLib.Chromium.Tests;

/// <summary>
/// Exercises the real pipe/process plumbing against a bash script that speaks just enough CDP.
/// </summary>
public class ChromiumBrowserLaunchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pdflib tests " + Guid.NewGuid().ToString("N"));

    public ChromiumBrowserLaunchTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteScript(string body)
    {
        var path = Path.Combine(_dir, "fake chrome.sh"); // note the space
        File.WriteAllText(path, "#!/bin/bash\n" + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private const string Responder = """
        while IFS= read -r -d '' msg <&3; do
          id=$(printf '%s' "$msg" | sed -n 's/.*"id":\([0-9]*\).*/\1/p')
          printf '{"id":%s,"result":{"product":"fake"}}\0' "$id" >&4
        done
        """;

    [Fact]
    public async Task Start_WorksWithSpacesInBinaryPath()
    {
        using var browser = new ChromiumBrowser();

        // StartAsync only returns once the child answered Browser.getVersion over fd 3/4
        await browser.StartAsync(new BrowserOptions { BinaryPath = WriteScript(Responder) })
            .WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Start_FailsFast_WithExitCode_WhenBrowserDiesImmediately()
    {
        using var browser = new ChromiumBrowser();
        var sw = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            browser.StartAsync(new BrowserOptions { BinaryPath = WriteScript("exit 7"), StartupTimeoutMs = 30_000 }));

        Assert.Contains("exit code 7", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), "should not wait out the startup timeout");
    }

    [Fact]
    public async Task Start_TimesOut_WhenBrowserNeverAnswers()
    {
        using var browser = new ChromiumBrowser();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            browser.StartAsync(new BrowserOptions { BinaryPath = WriteScript("sleep 30"), StartupTimeoutMs = 500 }));

        Assert.Contains("ready", ex.Message);
    }

    [Fact]
    public async Task Dispose_KillsTheBrowserProcessTree()
    {
        var pidFile = Path.Combine(_dir, "pid");
        var browser = new ChromiumBrowser();
        await browser.StartAsync(new BrowserOptions
        {
            BinaryPath = WriteScript($"echo $$ > '{pidFile}'\n" + Responder)
        });

        var pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
        browser.Dispose();

        for (var i = 0; i < 50 && Directory.Exists($"/proc/{pid}") && !IsZombie(pid); i++)
            await Task.Delay(100);
        Assert.True(!Directory.Exists($"/proc/{pid}") || IsZombie(pid));
    }

[Fact]
    public async Task ConcurrentStart_LaunchesOneBrowser_AndBothCallersWaitForIt()
    {
        var countFile = Path.Combine(_dir, "starts");
        using var browser = new ChromiumBrowser();
        var options = new BrowserOptions { BinaryPath = WriteScript($"echo x >> '{countFile}'\nsleep 0.3\n" + Responder) };

        var a = browser.StartAsync(options);
        var b = browser.StartAsync(options);
        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(await File.ReadAllLinesAsync(countFile));
    }

    [Fact]
    public async Task AdditionalArguments_ArePassedToTheBrowser()
    {
        var argsFile = Path.Combine(_dir, "args");
        using var browser = new ChromiumBrowser();
        await browser.StartAsync(new BrowserOptions
        {
            BinaryPath = WriteScript($"printf '%s\\n' \"$@\" > '{argsFile}'\n" + Responder),
            AdditionalArguments = { "--font-render-hinting=none", "--lang=en US" }
        }).WaitAsync(TimeSpan.FromSeconds(10));

        var args = await File.ReadAllLinesAsync(argsFile);
        Assert.Contains("--remote-debugging-pipe", args);
        Assert.Contains("--font-render-hinting=none", args);
        Assert.Contains("--lang=en US", args); // not split on the space
    }

    [Fact]
    public async Task CreatePage_BeforeStart_ExplainsWhatToDo()
    {
        using var browser = new ChromiumBrowser();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => browser.CreatePageAsync());
        Assert.Contains("StartAsync", ex.Message);
    }

        private static bool IsZombie(int pid)
    {
        try { return File.ReadAllText($"/proc/{pid}/stat").Contains(") Z "); }
        catch { return true; }
    }
}
