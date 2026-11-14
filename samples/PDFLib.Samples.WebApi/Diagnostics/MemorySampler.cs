using System.Globalization;

namespace PDFLib.Samples.WebApi.Diagnostics;

/// <summary>One memory reading. Sizes are in bytes.</summary>
/// <param name="ManagedHeap">Size of the .NET managed heap.</param>
/// <param name="AppWorkingSet">Physical memory used by this process (managed and native).</param>
/// <param name="Chromium">Physical memory used by the Chromium process tree, or null where it can't be measured.</param>
/// <param name="ChromiumProcesses">Number of processes in the Chromium tree.</param>
/// <param name="ChromiumMeasure">
/// How the Chromium figure was obtained: "pss" (shared memory counted once), "rss" (shared memory counted
/// once per process, which overstates the total several times over), "mixed" or "unavailable".
/// </param>
public readonly record struct MemoryReading(long ManagedHeap, long AppWorkingSet, long? Chromium, int ChromiumProcesses,
    string ChromiumMeasure = "unavailable");

/// <summary>Memory of one process in the Chromium tree. Sizes are in bytes.</summary>
/// <param name="Kind">browser, renderer, gpu-process, utility, zygote, or the command name for anything else.</param>
/// <param name="Pss">Proportional set size, or null if the kernel would not let us read it.</param>
/// <param name="Rss">Resident set size.</param>
public sealed record ProcessMemory(int Pid, string Kind, long? Pss, long Rss);

/// <summary>
/// Samples memory a few times a second and remembers the peaks, so a load test can ask afterwards how
/// high memory went rather than having to catch the peak itself.
/// </summary>
/// <remarks>
/// A PDF service has two memory consumers: this .NET process, and the Chromium processes it drives.
/// Chromium is usually the larger one, so reporting only the .NET side would be misleading. On Linux the
/// Chromium figure is the proportional set size (PSS) summed over every child process, which counts memory
/// shared between Chromium's processes once rather than once per process.
/// </remarks>
public sealed class MemorySampler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);
    private readonly object _gate = new();
    private MemoryReading _current;
    private MemoryReading _peak;

    public MemoryReading Current
    {
        get { lock (_gate) return _current; }
    }

    public MemoryReading Peak
    {
        get { lock (_gate) return _peak; }
    }

    public void ResetPeak()
    {
        lock (_gate) _peak = _current;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                var reading = Read();
                lock (_gate)
                {
                    _current = reading;
                    _peak = new MemoryReading(
                        Math.Max(_peak.ManagedHeap, reading.ManagedHeap),
                        Math.Max(_peak.AppWorkingSet, reading.AppWorkingSet),
                        reading.Chromium.HasValue ? Math.Max(_peak.Chromium ?? 0, reading.Chromium.Value) : _peak.Chromium,
                        Math.Max(_peak.ChromiumProcesses, reading.ChromiumProcesses),
                        reading.ChromiumMeasure);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down
        }
    }

    private static MemoryReading Read()
    {
        var processes = ReadChildProcesses();
        if (processes == null)
            return new MemoryReading(GC.GetTotalMemory(false), Environment.WorkingSet, null, 0);

        var withPss = processes.Count(p => p.Pss.HasValue);
        var measure = processes.Count == 0 ? "unavailable"
            : withPss == processes.Count ? "pss"
            : withPss == 0 ? "rss"
            : "mixed";

        return new MemoryReading(GC.GetTotalMemory(false), Environment.WorkingSet,
            processes.Sum(p => p.Pss ?? p.Rss), processes.Count, measure);
    }

    /// <summary>
    /// Lists every descendant of this process with its memory, by walking /proc. Linux only; returns
    /// null elsewhere or when /proc can't be read.
    /// </summary>
    public static IReadOnlyList<ProcessMemory>? ReadChildProcesses()
    {
        if (!OperatingSystem.IsLinux()) return null;

        try
        {
            // pid -> parent pid for everything we are allowed to see
            var parents = new Dictionary<int, int>();
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                if (TryReadParent(pid, out var parent)) parents[pid] = parent;
            }

            var children = parents.ToLookup(p => p.Value, p => p.Key);
            var pending = new Queue<int>(children[Environment.ProcessId]);
            var result = new List<ProcessMemory>();

            while (pending.Count > 0)
            {
                var pid = pending.Dequeue();
                foreach (var child in children[pid]) pending.Enqueue(child);

                var rss = ReadRss(pid);
                if (rss == null) continue; // exited while we were looking
                result.Add(new ProcessMemory(pid, ReadKind(pid), ReadPss(pid), rss.Value));
            }

            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryReadParent(int pid, out int parent)
    {
        parent = 0;
        try
        {
            // "pid (name) state ppid ...": the name can contain spaces and brackets, so cut after the last ')'
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return fields.Length > 1 && int.TryParse(fields[1], out parent);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // the process exited while we were looking
        }
    }

    /// <summary>
    /// Proportional set size: each shared page is divided by the number of processes sharing it, so the
    /// figures of several processes can be added up. Null when the kernel refuses access.
    /// </summary>
    private static long? ReadPss(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/smaps_rollup"))
            {
                if (!line.StartsWith("Pss:", StringComparison.Ordinal)) continue;
                var value = line.AsSpan(4).Trim();
                var kilobytes = long.Parse(value[..value.IndexOf(' ')], CultureInfo.InvariantCulture);
                return kilobytes * 1024;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            // not readable: the caller falls back to the resident set size
        }

        return null;
    }

    /// <summary>
    /// Resident set size. Counts shared pages in full for every process, so adding these up across
    /// Chromium's processes overstates the total.
    /// </summary>
    private static long? ReadRss(int pid)
    {
        try
        {
            var statm = File.ReadAllText($"/proc/{pid}/statm").Split(' ');
            return long.Parse(statm[1], CultureInfo.InvariantCulture) * Environment.SystemPageSize;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static string ReadKind(int pid)
    {
        try
        {
            // Normally arguments are separated by NUL bytes, but Chromium's helper processes rewrite their
            // command line as one space-separated string, so treat both the same way.
            var arguments = File.ReadAllText($"/proc/{pid}/cmdline")
                .Split(['\0', ' '], StringSplitOptions.RemoveEmptyEntries);

            // Chromium marks its helpers with --type=renderer, --type=gpu-process and so on
            var type = arguments.FirstOrDefault(a => a.StartsWith("--type=", StringComparison.Ordinal));
            if (type != null) return type["--type=".Length..];

            return arguments.Contains("--remote-debugging-pipe")
                ? "browser"
                : arguments.Length > 0 ? Path.GetFileName(arguments[0]) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
