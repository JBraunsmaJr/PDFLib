namespace PDFLib.Chromium;

/// <summary>
/// Options for configuring the <see cref="ChromiumBrowser"/>.
/// </summary>
public class BrowserOptions
{
    /// <summary>
    /// Gets or sets how many PDFs can be rendered at the same time. Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int MaxConcurrentRenders { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets the threshold of system memory (in MB) at which to pause new renders to prevent OOM.
    /// </summary>
    public long MemoryThresholdMb { get; set; } = 1024;

    /// <summary>
    /// Gets or sets how many idle pages (tabs) are kept warm for <see cref="ChromiumBrowser.RenderPdfAsync(string, Stream, CancellationToken)"/>
    /// and <see cref="ChromiumBrowser.RentPageAsync"/>. Creating a tab starts a new renderer process, which costs far more
    /// than the render itself for small documents. Each idle page keeps a renderer process alive, so this trades memory for speed.
    /// Set to 0 to create a fresh page for every render. Defaults to the smaller of <see cref="Environment.ProcessorCount"/> and 4.
    /// </summary>
    public int PagePoolSize { get; set; } = Math.Min(Environment.ProcessorCount, 4);

    /// <summary>
    /// Gets or sets how many renders a pooled page serves before it is closed and replaced, which bounds
    /// memory growth in long-lived renderer processes. Defaults to 100.
    /// </summary>
    public int MaxRendersPerPage { get; set; } = 100;

    /// <summary>
    /// Gets or sets the path to the Chromium binary. 
    /// If not set, the library will attempt to find the bundled binary in the runtimes folder,
    /// or fall back to "chrome-shell" in the system PATH.
    /// </summary>
    public string? BinaryPath { get; set; }

    /// <summary>
    /// Gets or sets how long (in milliseconds) to wait for Chromium to start answering commands. Defaults to 10 seconds.
    /// </summary>
    public int StartupTimeoutMs { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets whether PDFs carry accessibility tags (the structure tree used by screen readers and
    /// required for PDF/UA). Defaults to true, which is Chromium's own default.
    /// </summary>
    /// <remarks>
    /// Tagging is expensive for long documents: turning it off made a 420-page document about 30% faster
    /// to produce and roughly 8 times smaller. The pages look identical either way. Leave it on when the
    /// PDFs must be accessible.
    /// </remarks>
    public bool GenerateTaggedPdf { get; set; } = true;

    /// <summary>
    /// Gets or sets extra command line switches passed to Chromium after the built-in ones,
    /// e.g. <c>--font-render-hinting=none</c> or <c>--proxy-server=...</c>.
    /// </summary>
    public IList<string> AdditionalArguments { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the strategy to use when waiting for the page to be ready for rendering.
    /// </summary>
    public WaitStrategy WaitStrategy { get; set; } = WaitStrategy.Load;

    /// <summary>
    /// Gets or sets the name of the JavaScript variable to check if <see cref="WaitStrategy"/> is <see cref="WaitStrategy.JavascriptVariable"/>.
    /// </summary>
    public string? WaitVariable { get; set; }

    /// <summary>
    /// Gets or sets the expected value of the JavaScript variable if <see cref="WaitStrategy"/> is <see cref="WaitStrategy.JavascriptVariable"/>.
    /// </summary>
    public string? WaitVariableValue { get; set; }

    /// <summary>
    /// Gets or sets the maximum time to wait for the page to be ready (in milliseconds). Set to null for no timeout.
    /// </summary>
    public int? WaitTimeoutMs { get; set; } = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
}

/// <summary>
/// Specifies strategies for waiting for a page to be ready for rendering.
/// </summary>
[Flags]
public enum WaitStrategy
{
    /// <summary>
    /// Wait for document.readyState == 'complete'.
    /// </summary>
    Load = 1,

    /// <summary>
    /// Wait until there are no more than 2 network connections for at least 500ms. This is from
    /// Chromium itself, not the library
    /// </summary>
    NetworkIdle = 2,

    /// <summary>
    /// Wait for a specific JavaScript variable to equal a specific value.
    /// </summary>
    JavascriptVariable = 4
}