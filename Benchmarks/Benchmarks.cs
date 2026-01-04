using BenchmarkDotNet.Attributes;

namespace Benchmarks;

[MemoryDiagnoser]
public class Benchmarks
{
    private IConverter _dink;
    private string _html;

    [GlobalSetup]
    public void Setup()
    {
        _dink = new DinkPdf();
        _html = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.html"));
    }

    [Benchmark]
    public void Dink()
    {
        _dink.Convert(_html);
    }
}