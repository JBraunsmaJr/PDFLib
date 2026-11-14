using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PDFLib.Chromium.RPC;

namespace PDFLib.Chromium;

/// <summary>
/// Provides a wrapper around the Chromium browser process for PDF generation.
/// </summary>
public class ChromiumBrowser : IDisposable
{
    private static ChromiumBrowser? _instance;
    private CdpDispatcher _dispatcher = null!;

    private bool _disposed;
    private readonly object _startLock = new();
    private Task? _startTask;
    private Stream? _pipeWriter;
    private readonly ConcurrentQueue<CdpPage> _pagePool = new();
    private int _pooledPages;
    private BrowserOptions _options = new();
    private Process? _process;
    private SemaphoreSlim _renderSemaphore = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromiumBrowser"/> class.
    /// </summary>
    public ChromiumBrowser()
    {
        _instance ??= this;
    }


    /// <summary>
    /// Gets the singleton instance of the <see cref="ChromiumBrowser"/>.
    /// </summary>
    public static ChromiumBrowser Instance
    {
        get
        {
            _instance ??= new ChromiumBrowser();
            return _instance;
        }
    }

    /// <summary>
    /// Disposes of the browser process and associated resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process != null)
            try
            {
                if (!_process.HasExited) _process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                /* Already exited or never started */
            }
            catch (Exception)
            {
                /* Best effort */
            }
            finally
            {
                _process.Dispose();
            }

        // Closing our write end also lets a browser we failed to kill see EOF and exit on its own
        try
        {
            _pipeWriter?.Dispose();
        }
        catch (Exception)
        {
            /* Best effort */
        }

        if (_instance == this) _instance = null;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe2(int[] pipefd, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private const int O_CLOEXEC = 0x80000;
    private const int F_SETFD = 2;
    private const int F_SETPIPE_SZ = 1031;
    private const int PipeSize = 1024 * 1024;

    private string ResolveBinaryPath()
    {
        if (!string.IsNullOrEmpty(_options.BinaryPath))
        {
            return _options.BinaryPath;
        }
        
        /*
         * If installed via Nuget, this is where the binary should be installed
         * runtimes/linux-x64/native/chrome-headless-shell
         */
        var baseDir = AppContext.BaseDirectory;
        var bundledPath = Path.Combine(baseDir, "runtimes", "linux-x64", "native", "chrome-headless-shell");
        
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        /*
         * For RID-specific builds (flattened output) or local builds 
         * it should be in the root application directory
         */
        var flattenedPath = Path.Combine(baseDir, "chrome-headless-shell");
        if (File.Exists(flattenedPath))
        {
            return flattenedPath;
        }

        // Fallback to system PATH
        return "chrome-shell";
    }

    /// <summary>
    /// Starts the Chromium browser process with the given options.
    /// </summary>
    /// <param name="options">The configuration options for the browser.</param>
    /// <returns>A task that represents the asynchronous start operation.</returns>
    /// <exception cref="Exception">Thrown if the browser fails to start or become ready.</exception>
    public Task StartAsync(BrowserOptions? options)
    {
        // Every caller gets the same task, so a second caller can't spawn a second browser and
        // doesn't return before the browser is actually ready either.
        lock (_startLock)
        {
            return _startTask ??= StartCoreAsync(options);
        }
    }

    private async Task StartCoreAsync(BrowserOptions? options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _options = options ?? new BrowserOptions();
        _renderSemaphore = new SemaphoreSlim(_options.MaxConcurrentRenders);

        var binaryPath = ResolveBinaryPath();

        var pipeOut = new int[2]; // C# -> Chrome
        var pipeIn = new int[2]; // Chrome -> C#

        /*
         * CLOEXEC on everything first so the browser only inherits the two ends we hand it.
         * Previously our own ends leaked into Chrome, which kept the pipes open after this process died
         * (Chrome never saw EOF and could be orphaned) and meant we never saw EOF if Chrome died.
         */
        if (pipe2(pipeOut, O_CLOEXEC) != 0 || pipe2(pipeIn, O_CLOEXEC) != 0)
            throw new IOException("Failed to create Linux pipes");

        // Clear FD_CLOEXEC on the child's ends so they survive the exec into bash
        fcntl(pipeOut[0], F_SETFD, 0);
        fcntl(pipeIn[1], F_SETFD, 0);

        // Bigger kernel buffers (default 64KB) mean fewer context switches per PDF chunk / HTML payload.
        // Best effort: fails harmlessly if /proc/sys/fs/pipe-max-size is lower.
        fcntl(pipeOut[1], F_SETPIPE_SZ, PipeSize);
        fcntl(pipeIn[0], F_SETPIPE_SZ, PipeSize);

        /*
         * The binary path is passed as a positional parameter ($0 / "$@") rather than interpolated into the
         * script, so paths with spaces or shell metacharacters work and can't inject commands.
         */
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        // Chromium expects the pipe on fds 3 and 4. Close the original numbers afterwards so it doesn't
        // carry stray duplicates around.
        var script = $"exec 3<&{pipeOut[0]} 4>&{pipeIn[1]}";
        if (pipeOut[0] > 4) script += $" {pipeOut[0]}<&-";
        if (pipeIn[1] > 4) script += $" {pipeIn[1]}>&-";
        startInfo.ArgumentList.Add(script + "; exec \"$@\"");
        startInfo.ArgumentList.Add("chrome"); // $0
        startInfo.ArgumentList.Add(binaryPath);
        foreach (var arg in LaunchArguments) startInfo.ArgumentList.Add(arg);
        foreach (var arg in _options.AdditionalArguments) startInfo.ArgumentList.Add(arg);

        try
        {
            _process = Process.Start(startInfo);
        }
        catch
        {
            close(pipeOut[1]);
            close(pipeIn[0]);
            throw;
        }
        finally
        {
            // Child has its own copies now. Closing ours is what makes EOF/exit detection work.
            close(pipeOut[0]);
            close(pipeIn[1]);
        }

        var writer = new FileStream(new SafeFileHandle(pipeOut[1], true), FileAccess.Write);
        _pipeWriter = writer;
        var reader = new FileStream(new SafeFileHandle(pipeIn[0], true), FileAccess.Read);

        _dispatcher = new CdpDispatcher(writer, reader);

        /*
         * Chromium buffers whatever we write to the pipe until it is up, so a single Browser.getVersion
         * both probes readiness and can't be lost. The timeout is enforced with a token: the old loop's
         * deadline was only checked between attempts and never fired if Chromium simply never answered.
         */
        using var startupCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.StartupTimeoutMs));
        try
        {
            using var res = await _dispatcher.SendCommandAsync("Browser.getVersion", cancellationToken: startupCts.Token);
        }
        catch (Exception ex)
        {
            // bash may take a moment to be reaped after the pipe closes
            _process?.WaitForExit(500);
            int? exitCode = _process is { HasExited: true } ? _process.ExitCode : null;
            Dispose();
            throw new InvalidOperationException(
                exitCode.HasValue
                    ? $"Chromium exited during startup (exit code {exitCode}). Check BinaryPath ('{binaryPath}') and that its shared libraries are installed."
                    : $"Chromium failed to become ready within {_options.StartupTimeoutMs}ms.",
                ex);
        }
    }

    private static readonly string[] LaunchArguments =
    [
        "--headless", "--remote-debugging-pipe", "--no-sandbox",
        "--disable-gpu", "--disable-dev-shm-usage", "--disable-software-rasterizer", "--disable-extensions",
        "--disable-background-networking", "--disable-sync", "--disable-default-apps", "--mute-audio"
    ];

    /// <summary>
    /// Creates a new page (tab) in the browser.
    /// </summary>
    /// <returns>A task that represents the asynchronous creation operation. The task result is a <see cref="CdpPage"/>.</returns>
    public async Task<CdpPage> CreatePageAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var startTask = _startTask ?? throw new InvalidOperationException(
            $"The browser has not been started. Call {nameof(StartAsync)} first (or register it with AddPdfService).");
        await startTask;

        using var targetRes = await _dispatcher.SendCommandAsync("Target.createTarget", new { url = "about:blank" });
        var targetId = targetRes?.RootElement.GetProperty("targetId").GetString()
                       ?? throw new InvalidOperationException("Chromium did not return a targetId.");

        string sessionId;
        try
        {
            using var attachRes = await _dispatcher.SendCommandAsync("Target.attachToTarget", new { targetId, flatten = true });
            sessionId = attachRes?.RootElement.GetProperty("sessionId").GetString()
                        ?? throw new InvalidOperationException("Chromium did not return a sessionId.");
        }
        catch
        {
            // Don't leak the tab if we couldn't attach to it
            try
            {
                using var closeRes = await _dispatcher.SendCommandAsync("Target.closeTarget", new { targetId });
            }
            catch
            {
                /* Best effort */
            }

            throw;
        }

        return new CdpPage(_dispatcher, sessionId, targetId, _renderSemaphore, _options);
    }

    /// <summary>
    /// Gets a ready-to-use page, reusing a warm one from the pool when available.
    /// Hand it back with <see cref="ReturnPageAsync"/> when done (or dispose it if something went wrong).
    /// </summary>
    /// <returns>A page showing a blank document.</returns>
    public async Task<CdpPage> RentPageAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pagePool.TryDequeue(out var page)) Interlocked.Decrement(ref _pooledPages);
        else page = await CreatePageAsync();

        page.RentCount++;
        return page;
    }

    /// <summary>
    /// Returns a page obtained from <see cref="RentPageAsync"/>. The page is reset to a blank document and kept
    /// for reuse, or closed if the pool is full, the page has served <see cref="BrowserOptions.MaxRendersPerPage"/>
    /// renders, or it could not be reset. Never throws.
    /// </summary>
    /// <param name="page">The page to return. Do not use it afterwards.</param>
    public async ValueTask ReturnPageAsync(CdpPage page)
    {
        if (!_disposed && _options.PagePoolSize > 0 && page.RentCount < _options.MaxRendersPerPage)
        {
            try
            {
                using var resetCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await page.ResetAsync(resetCts.Token);

                if (Interlocked.Increment(ref _pooledPages) <= _options.PagePoolSize)
                {
                    _pagePool.Enqueue(page);
                    return;
                }

                Interlocked.Decrement(ref _pooledPages);
            }
            catch
            {
                // Fall through and close it; a page we can't reset must not be reused
            }
        }

        await page.DisposeAsync();
    }

    /// <summary>
    /// Renders HTML to a PDF using a pooled page. This is the simplest and fastest way to produce a PDF.
    /// </summary>
    /// <param name="html">The HTML to convert into PDF.</param>
    /// <param name="destinationStream">The stream where the PDF will be written.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RenderPdfAsync(string html, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        return RenderPdfAsync(html, destinationStream, null, cancellationToken);
    }

    /// <summary>
    /// Renders HTML to a PDF using a pooled page and returns the signature zones found in the document.
    /// </summary>
    /// <param name="html">The HTML to convert into PDF.</param>
    /// <param name="destinationStream">The stream where the PDF will be written.</param>
    /// <param name="signatureData">Optional dictionary of signature data to inject into the DOM before printing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of signature zones found in the document.</returns>
    public async Task<List<SignatureZone>> RenderPdfAsync(string html, Stream destinationStream,
        Dictionary<string, Signature>? signatureData, CancellationToken cancellationToken = default)
    {
        var page = await RentPageAsync();
        List<SignatureZone> zones;
        try
        {
            zones = await page.PrintToPdfAsync(html, destinationStream, signatureData, cancellationToken);
        }
        catch
        {
            // Unknown state (cancelled mid-stream, crashed tab, ...): never put it back in the pool
            await page.DisposeAsync();
            throw;
        }

        await ReturnPageAsync(page);
        return zones;
    }
}
