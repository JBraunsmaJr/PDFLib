using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

BenchmarkRunner.Run<Benchmarks.Benchmarks>(new AllowNonOptimized(), args);

public class AllowNonOptimized : ManualConfig
{
    public AllowNonOptimized()
    {
        AddValidator(JitOptimizationsValidator.DontFailOnError);
        AddLogger(DefaultConfig.Instance.GetLoggers().ToArray());
        AddExporter(DefaultConfig.Instance.GetExporters().ToArray());
        AddColumnProvider(DefaultConfig.Instance.GetColumnProviders().ToArray());
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Error);
        AddColumn(StatisticColumn.Max);
        // The in-process toolchain aborts a benchmark case after 5 minutes by default, which the larger
        // samples can exceed.
        AddJob(Job.Default.WithToolchain(new InProcessEmitToolchain(TimeSpan.FromHours(1), logOutput: true)));
    }
}
