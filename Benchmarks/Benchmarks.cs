using BenchmarkDotNet.Attributes;

namespace Benchmarks;

[MemoryDiagnoser]
public class Benchmarks
{
    private IConverter _dink;
    private IConverter _pdfLib;

    private string _html;

    private IEnumerable<IConverter> Converters()
    {
        yield return _dink;
        yield return _pdfLib;
    }
    
    [GlobalSetup]
    public void Setup()
    {
        _dink = new DinkPdf();
        _pdfLib = new PdfLib();

        _html = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample.html"));
        foreach(var converter in Converters()) converter.GlobalSetup();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        foreach(var converter in Converters()) converter.GlobalCleanup();;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach(var converter in Converters()) converter.IterationCleanup();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        foreach(var converter in Converters()) converter.IterationSetup();;
    }

    [Benchmark]
    public void Dink()
    {
        _dink.Convert(_html);
    }

    [Benchmark]
    public void PdfLib()
    {
        _pdfLib.Convert(_html);
    }
}