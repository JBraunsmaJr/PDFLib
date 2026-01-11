using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PDFLib.Chromium.RPC;

namespace PDFLib.Chromium;

public class ChromiumBrowser : IDisposable
{
    private Process? _process;
    private CdpDispatcher _dispatcher;
    private SemaphoreSlim _renderSemaphore;
    private BrowserOptions _options;

    
    public static ChromiumBrowser Instance
    {
        get
        {
            _instance ??= new ChromiumBrowser();
            return _instance;
        }
    }
    private bool _hasStarted;
    private static ChromiumBrowser? _instance;
    
    [DllImport("libc", SetLastError = true)] private static extern int pipe(int[] pipefd);
    [DllImport("libc", SetLastError = true)] private static extern int fcntl(int fd, int cmd, int arg);

    public ChromiumBrowser()
    {
        if (_instance is null) _instance = this;
    }
    
    /// <summary>
    /// Start the Chromium browser process with the given options
    /// </summary>
    /// <param name="options"></param>
    /// <exception cref="Exception"></exception>
    public async Task StartAsync(BrowserOptions? options)
    {
        if (_hasStarted) return;
        _hasStarted = true;
        
        _options = options ?? new BrowserOptions();
        _renderSemaphore = new(_options.MaxConcurrentRenders);
        
        var pipeOut = new int[2]; // C# -> Chrome
        var pipeIn = new int[2];  // Chrome -> C#
        
        if (pipe(pipeOut) != 0 || pipe(pipeIn) != 0) throw new Exception("Failed to create Linux pipes");

        // Clear FD_CLOEXEC so child inherits handles
        fcntl(pipeOut[0], 2, 0); 
        fcntl(pipeIn[1], 2, 0);

        var shellCmd = $"exec 3<&{pipeOut[0]} 4>&{pipeIn[1]}; exec {_options.BinaryPath} --headless --remote-debugging-pipe --no-sandbox --disable-gpu --disable-dev-shm-usage --disable-software-rasterizer --disable-extensions --disable-background-networking --disable-sync --disable-default-apps --mute-audio";
        
        _process = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{shellCmd}\"",
            UseShellExecute = false
        });

        var writer = new FileStream(new SafeFileHandle(pipeOut[1], true), FileAccess.Write);
        var reader = new FileStream(new SafeFileHandle(pipeIn[0], true), FileAccess.Read);

        _dispatcher = new CdpDispatcher(writer, reader);
        
        // Wait for Chromium to be ready by sending a simple command instead of a fixed delay
        var ready = false;
        var start = Stopwatch.GetTimestamp();
        
        while (!ready && (Stopwatch.GetTimestamp() - start) < (long)Stopwatch.Frequency * 5)
        {
            try
            {
                await _dispatcher.SendCommandAsync("Browser.getVersion");
                ready = true;
            }
            catch
            {
                await Task.Delay(50);
            }
        }

        if (!ready) throw new Exception("Chromium failed to become ready within timeout");
    }

    public async Task<CdpPage> CreatePageAsync()
    {
        var targetRes = await _dispatcher.SendCommandAsync("Target.createTarget", new { url = "about:blank" });
        var targetId = targetRes.GetProperty("targetId").GetString();

        var attachRes = await _dispatcher.SendCommandAsync("Target.attachToTarget", new { targetId, flatten = true });
        var sessionId = attachRes.GetProperty("sessionId").GetString();

        return new CdpPage(_dispatcher, sessionId, targetId, _renderSemaphore, _options);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch (InvalidOperationException) { /* Already exited or never started */ }
            catch (Exception) { /* Best effort */ }
            finally
            {
                _process.Dispose();
            }
        }

        if (_instance == this)
        {
            _instance = null;
        }
    }
}