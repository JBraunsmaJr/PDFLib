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
}