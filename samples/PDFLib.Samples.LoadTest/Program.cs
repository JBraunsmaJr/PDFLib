using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

/*
 * Load test for the sample API.
 *
 * For each concurrency level it runs that many clients back to back for a fixed time. Every client sends a
 * request, reads the whole PDF, checks it, and immediately sends the next one. Afterwards it asks the API
 * for the peak memory it saw during that level (the .NET process and the Chromium process tree), and
 * reports throughput in requests per minute together with latency percentiles.
 *
 *   dotnet run -c Release -- --url http://localhost:5080 --levels 1,2,4,8,16 --duration 20
 */

var options = Options.Parse(args);
if (options == null) return 2;

using var http = new HttpClient(new SocketsHttpHandler
{
    MaxConnectionsPerServer = 1024,
    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
})
{
    BaseAddress = new Uri(options.Url),
    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
};

JsonElement serverOptions;
var chromiumMeasure = "unavailable";
try
{
    using var stats = JsonDocument.Parse(await http.GetStringAsync("/stats"));
    serverOptions = stats.RootElement.GetProperty("options").Clone();
    if (stats.RootElement.GetProperty("memoryMb").TryGetProperty("chromiumMeasure", out var measure))
        chromiumMeasure = measure.GetString() ?? chromiumMeasure;
}
catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
{
    Console.Error.WriteLine($"Could not reach the sample API at {options.Url}: {e.Message}");
    Console.Error.WriteLine("Start it first: dotnet run -c Release --project samples/PDFLib.Samples.WebApi");
    return 2;
}

Console.WriteLine($"Target     {options.Url}");
Console.WriteLine($"Scenario   {options.Scenario}");
Console.WriteLine($"Levels     {string.Join(", ", options.Levels)} concurrent clients, {options.DurationSeconds}s each");
Console.WriteLine($"Server     {serverOptions}");
Console.WriteLine($"Memory     Chromium measured as {Report.DescribeMeasure(chromiumMeasure)}");
Console.WriteLine();

if (options.WarmupSeconds > 0)
{
    Console.Write($"Warming up for {options.WarmupSeconds}s... ");
    await RunLevel(http, options, concurrency: 2, TimeSpan.FromSeconds(options.WarmupSeconds));
    Console.WriteLine("done");
    Console.WriteLine();
}

Console.WriteLine($"{"Clients",7} {"Requests",9} {"Req/min",9} {"Mean",8} {"p50",8} {"p95",8} {"p99",8} {"Errors",7} {"503s",6} {"App MB",8} {"Chromium MB",12}");

var results = new List<LevelResult>();
foreach (var level in options.Levels)
{
    var result = await RunLevel(http, options, level, TimeSpan.FromSeconds(options.DurationSeconds));
    results.Add(result);

    Console.WriteLine(
        $"{result.Concurrency,7} {result.Succeeded,9} {result.RequestsPerMinute,9:F0} {result.Mean,6:F0}ms {result.P50,6:F0}ms {result.P95,6:F0}ms {result.P99,6:F0}ms " +
        $"{result.Errors,7} {result.Rejected,6} {Format(result.PeakAppMb),8} {Format(result.PeakChromiumMb),12}");
}

var markdownPath = options.Output + ".md";
var csvPath = options.Output + ".csv";
await File.WriteAllTextAsync(markdownPath, Report.Markdown(options, serverOptions, chromiumMeasure, results));
await File.WriteAllTextAsync(csvPath, Report.Csv(results));

Console.WriteLine();
Console.WriteLine($"Wrote {markdownPath} and {csvPath}");

var worst = results.Max(r => r.ErrorRate);
if (worst > options.MaxErrorRate)
{
    Console.Error.WriteLine($"FAILED: error rate {worst:P1} exceeds the allowed {options.MaxErrorRate:P1}.");
    return 1;
}

return 0;

static string Format(double? value) => value.HasValue ? value.Value.ToString("F0", CultureInfo.InvariantCulture) : "n/a";

static async Task<LevelResult> RunLevel(HttpClient http, Options options, int concurrency, TimeSpan duration)
{
    // Start each level from a clean slate so its peaks are its own
    using (await http.PostAsync("/stats/reset", null)) { }

    var samples = new List<Sample>[concurrency];
    var clock = Stopwatch.StartNew();

    var clients = Enumerable.Range(0, concurrency).Select(index => Task.Run(async () =>
    {
        var mine = samples[index] = new List<Sample>(1024);
        var random = new Random(index * 7919 + concurrency);
        var buffer = new byte[64 * 1024];

        while (clock.Elapsed < duration)
        {
            var request = Scenarios.Next(options.Scenario, random);
            var started = Stopwatch.GetTimestamp();
            var status = 0;
            long bytes = 0;
            var isPdf = false;

            try
            {
                using var message = new HttpRequestMessage(request.Method, request.Path);
                using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
                status = (int)response.StatusCode;

                // Read the whole body: a PDF isn't delivered until its last byte arrives
                await using var body = await response.Content.ReadAsStreamAsync();
                int read;
                while ((read = await body.ReadAsync(buffer)) > 0)
                {
                    if (bytes == 0) isPdf = read >= 5 && buffer.AsSpan(0, 5).SequenceEqual("%PDF-"u8);
                    bytes += read;
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                status = -1; // connection failure or timeout
            }

            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            mine.Add(new Sample(request.Name, elapsed, status, bytes, status is >= 200 and < 300 && isPdf));

            // Turned away because the API's queue is full: back off briefly instead of hammering it
            if (status == 503) await Task.Delay(100);
        }
    })).ToArray();

    await Task.WhenAll(clients);
    var elapsedSeconds = clock.Elapsed.TotalSeconds;

    // Give the API's memory sampler one more tick before reading the peaks
    await Task.Delay(700);
    double? peakApp = null, peakChromium = null, peakHeap = null;
    using (var stats = JsonDocument.Parse(await http.GetStringAsync("/stats")))
    {
        var peak = stats.RootElement.GetProperty("peakMemoryMb");
        peakApp = peak.GetProperty("appWorkingSet").GetDouble();
        peakHeap = peak.GetProperty("managedHeap").GetDouble();
        if (peak.GetProperty("chromium").ValueKind == JsonValueKind.Number) peakChromium = peak.GetProperty("chromium").GetDouble();
    }

    return LevelResult.From(concurrency, elapsedSeconds, samples.SelectMany(s => s).ToList(), peakApp, peakHeap, peakChromium);
}

internal sealed record Sample(string Endpoint, double Milliseconds, int Status, long Bytes, bool Ok);

internal sealed record EndpointResult(string Endpoint, int Requests, double Mean, double P95, double AverageKb);

internal sealed record LevelResult(
    int Concurrency,
    double ElapsedSeconds,
    int Succeeded,
    int Errors,
    int Rejected,
    double RequestsPerMinute,
    double Mean,
    double P50,
    double P95,
    double P99,
    double MegabytesReceived,
    double? PeakAppMb,
    double? PeakManagedHeapMb,
    double? PeakChromiumMb,
    IReadOnlyList<EndpointResult> Endpoints)
{
    public double ErrorRate => Succeeded + Errors == 0 ? 1 : (double)Errors / (Succeeded + Errors);

    public static LevelResult From(int concurrency, double elapsedSeconds, List<Sample> samples,
        double? peakApp, double? peakHeap, double? peakChromium)
    {
        var ok = samples.Where(s => s.Ok).ToList();
        var latencies = ok.Select(s => s.Milliseconds).Order().ToArray();

        // 503 means the API's queue was full and it shed the request, which is by design; count it apart
        var rejected = samples.Count(s => s.Status == 503);
        var errors = samples.Count - ok.Count - rejected;

        var endpoints = ok.GroupBy(s => s.Endpoint)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var sorted = g.Select(s => s.Milliseconds).Order().ToArray();
                return new EndpointResult(g.Key, sorted.Length, sorted.Average(), Percentile(sorted, 0.95), g.Average(s => s.Bytes) / 1024.0);
            })
            .ToList();

        return new LevelResult(
            concurrency, elapsedSeconds, ok.Count, errors, rejected,
            ok.Count / elapsedSeconds * 60,
            latencies.Length > 0 ? latencies.Average() : 0,
            Percentile(latencies, 0.50), Percentile(latencies, 0.95), Percentile(latencies, 0.99),
            ok.Sum(s => s.Bytes) / 1024.0 / 1024.0,
            peakApp, peakHeap, peakChromium, endpoints);
    }

    /// <summary>Nearest-rank percentile of an ascending array.</summary>
    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        var rank = (int)Math.Ceiling(percentile * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

internal static class Scenarios
{
    public static readonly string[] Names = ["mix", "invoice", "statement", "sales", "contract"];

    public sealed record Request(string Name, HttpMethod Method, string Path);

    /// <summary>
    /// Picks the next request. Ids are random, so every request renders a different document, as real
    /// traffic would, rather than the same one over and over.
    /// </summary>
    public static Request Next(string scenario, Random random)
    {
        if (scenario == "mix")
        {
            // Mostly short documents, with some long and some signed ones in between
            scenario = random.Next(100) switch
            {
                < 50 => "invoice",
                < 70 => "statement",
                < 90 => "sales",
                _ => "contract"
            };
        }

        return scenario switch
        {
            "invoice" => new Request("invoice", HttpMethod.Get, $"/reports/invoice/{random.Next(1000, 99999)}"),
            "statement" => new Request("statement", HttpMethod.Get, $"/reports/statement/{random.Next(1000, 99999)}?months=3"),
            "sales" => new Request("sales", HttpMethod.Get, $"/reports/sales/{random.Next(2015, 2031)}"),
            "contract" => new Request("contract", HttpMethod.Post, $"/contracts/SA-{random.Next(100000, 999999)}"),
            _ => throw new ArgumentException($"Unknown scenario '{scenario}'.")
        };
    }
}

internal sealed class Options
{
    public string Url { get; private set; } = "http://localhost:5080";
    public int[] Levels { get; private set; } = [1, 2, 4, 8, 16];
    public int DurationSeconds { get; private set; } = 20;
    public int WarmupSeconds { get; private set; } = 5;
    public int TimeoutSeconds { get; private set; } = 120;
    public string Scenario { get; private set; } = "mix";
    public string Output { get; private set; } = "loadtest-results";
    public double MaxErrorRate { get; private set; } = 0.01;

    public static Options? Parse(string[] args)
    {
        var options = new Options();
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => i + 1 < args.Length ? args[++i] : throw new FormatException($"{args[i]} needs a value.");

                switch (args[i])
                {
                    case "--url": options.Url = Value().TrimEnd('/'); break;
                    case "--levels":
                        options.Levels = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                        break;
                    case "--duration": options.DurationSeconds = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--warmup": options.WarmupSeconds = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--timeout": options.TimeoutSeconds = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--scenario": options.Scenario = Value(); break;
                    case "--out": options.Output = Value(); break;
                    case "--max-error-rate": options.MaxErrorRate = double.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "-h" or "--help":
                        PrintUsage();
                        return null;
                    default:
                        throw new FormatException($"Unknown option '{args[i]}'.");
                }
            }

            if (options.Levels.Length == 0 || options.Levels.Any(l => l < 1)) throw new FormatException("--levels needs positive numbers.");
            if (options.DurationSeconds < 1) throw new FormatException("--duration must be at least 1.");
            if (!Scenarios.Names.Contains(options.Scenario))
                throw new FormatException($"--scenario must be one of: {string.Join(", ", Scenarios.Names)}.");
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return null;
        }

        return options;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Load test for the PDFLib sample API.

              --url <url>              Base address of the API            (default http://localhost:5080)
              --levels <n,n,...>       Concurrent clients, one run each   (default 1,2,4,8,16)
              --duration <seconds>     Length of each run                 (default 20)
              --warmup <seconds>       Warm-up before the first run       (default 5, 0 to skip)
              --scenario <name>        mix, invoice, statement, sales or contract (default mix)
              --timeout <seconds>      Per-request timeout                (default 120)
              --out <path>             Results are written to <path>.md and <path>.csv (default loadtest-results)
              --max-error-rate <0..1>  Exit with code 1 above this error rate (default 0.01)
            """);
    }
}

internal static class Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Mb(double? value) => value.HasValue ? value.Value.ToString("F0", Inv) : "n/a";

    public static string DescribeMeasure(string measure) => measure switch
    {
        "pss" => "PSS (memory shared between Chromium's processes is counted once)",
        "rss" => "RSS (shared memory is counted once per process, so the total is overstated, typically two to three times)",
        "mixed" => "a mix of PSS and RSS (some processes could not be read precisely, so the total is overstated)",
        _ => "unavailable on this platform"
    };

    public static string Markdown(Options options, JsonElement server, string chromiumMeasure, List<LevelResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Sample API load test");
        sb.AppendLine();
        sb.AppendLine($"- **When:** {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", Inv)} UTC");
        sb.AppendLine($"- **Scenario:** `{options.Scenario}`, {options.DurationSeconds}s per level after {options.WarmupSeconds}s warm-up");
        sb.AppendLine($"- **Client machine:** {Environment.ProcessorCount} logical cores, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"- **Server options:** `{server}`");
        sb.AppendLine($"- **Chromium memory:** {DescribeMeasure(chromiumMeasure)}");
        sb.AppendLine();
        sb.AppendLine("## Throughput and memory");
        sb.AppendLine();
        sb.AppendLine("| Clients | Requests | Req/min | Mean | p50 | p95 | p99 | Errors | 503s | Peak app MB | Peak Chromium MB | Peak total MB |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in results)
        {
            double? total = r.PeakAppMb.HasValue && r.PeakChromiumMb.HasValue ? r.PeakAppMb + r.PeakChromiumMb : null;
            sb.AppendLine(string.Create(Inv,
                $"| {r.Concurrency} | {r.Succeeded} | {r.RequestsPerMinute:F0} | {r.Mean:F0} ms | {r.P50:F0} ms | {r.P95:F0} ms | {r.P99:F0} ms | {r.Errors} | {r.Rejected} | {Mb(r.PeakAppMb)} | {Mb(r.PeakChromiumMb)} | {Mb(total)} |"));
        }

        sb.AppendLine();
        sb.AppendLine("Requests per minute counts completed, valid PDFs. \"Peak app\" is the working set of the .NET process; ");
        sb.AppendLine("\"Peak Chromium\" is the memory of the whole Chromium process tree. 503s are requests the API turned away because its queue was full.");
        sb.AppendLine();
        sb.AppendLine("## By report type");
        sb.AppendLine();
        sb.AppendLine("| Clients | Report | Requests | Mean | p95 | Average size |");
        sb.AppendLine("|---:|---|---:|---:|---:|---:|");
        foreach (var r in results)
        foreach (var e in r.Endpoints)
            sb.AppendLine(string.Create(Inv, $"| {r.Concurrency} | {e.Endpoint} | {e.Requests} | {e.Mean:F0} ms | {e.P95:F0} ms | {e.AverageKb:F0} KB |"));

        return sb.ToString();
    }

    public static string Csv(List<LevelResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("clients,requests,requests_per_minute,mean_ms,p50_ms,p95_ms,p99_ms,errors,rejected_503,megabytes_received,peak_app_mb,peak_managed_heap_mb,peak_chromium_mb");
        foreach (var r in results)
            sb.AppendLine(string.Create(Inv,
                $"{r.Concurrency},{r.Succeeded},{r.RequestsPerMinute:F1},{r.Mean:F1},{r.P50:F1},{r.P95:F1},{r.P99:F1},{r.Errors},{r.Rejected},{r.MegabytesReceived:F1},{r.PeakAppMb},{r.PeakManagedHeapMb},{r.PeakChromiumMb}"));
        return sb.ToString();
    }
}
