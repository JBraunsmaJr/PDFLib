namespace PDFLib.Console;

public class BrowserOptions
{
    /// <summary>
    /// How many PDFs can be rendered at the same time. Defaults to Environment.ProcessorCount
    /// </summary>
    public int MaxConcurrentRenders { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Threshold of system memory (in MB) at which we should pause new renders to
    /// prevent the Linux OOM from nuking the process
    /// </summary>
    public long MemoryThresholdMb { get; set; } = 1024;

    public string BinaryPath { get; set; } = "chrome-shell";

    /// <summary>
    /// Strategy to use when waiting for the page to be ready for rendering.
    /// </summary>
    public WaitStrategy WaitStrategy { get; set; } = WaitStrategy.Load;

    /// <summary>
    /// If WaitStrategy is JavascriptVariable, this is the name of the variable to check.
    /// </summary>
    public string? WaitVariable { get; set; }

    /// <summary>
    /// If WaitStrategy is JavascriptVariable, this is the expected value of the variable.
    /// </summary>
    public string? WaitVariableValue { get; set; }

    /// <summary>
    /// Maximum time to wait for the page to be ready (in milliseconds). Set to null for no timeout.
    /// </summary>
    public int? WaitTimeoutMs { get; set; } = 10000;
}

[System.Flags]
public enum WaitStrategy
{
    /// <summary>
    /// Wait for document.readyState == 'complete'
    /// </summary>
    Load = 1,
    /// <summary>
    /// Wait until there are no more than 2 network connections for at least 500ms.
    /// </summary>
    NetworkIdle = 2,
    /// <summary>
    /// Wait for a specific javascript variable to equal a specific value.
    /// </summary>
    JavascriptVariable = 4
}