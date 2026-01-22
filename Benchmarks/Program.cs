using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using BenchmarkDotNet.Columns;

BenchmarkRunner.Run<Benchmarks.Benchmarks>(new AllowNonOptimized());

public class AllowNonOptimized : ManualConfig
{
    public AllowNonOptimized()
    {
        Add(JitOptimizationsValidator.DontFailOnError);
        Add(DefaultConfig.Instance.GetLoggers().ToArray());
        Add(DefaultConfig.Instance.GetExporters().ToArray());
        Add(DefaultConfig.Instance.GetColumnProviders().ToArray());
        Add(StatisticColumn.Mean);
        Add(StatisticColumn.StdDev);
        Add(StatisticColumn.Error);
        Add(StatisticColumn.Max);
    }
}