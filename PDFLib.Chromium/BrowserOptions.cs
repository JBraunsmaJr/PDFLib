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
    /// Gets or sets the path to the Chromium binary. 
    /// If not set, the library will attempt to find the bundled binary in the runtimes folder,
    /// or fall back to "chrome-shell" in the system PATH.
    /// </summary>
    public string? BinaryPath { get; set; }

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
    public int? WaitTimeoutMs { get; set; } = TimeSpan.FromSeconds(10).Milliseconds;
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