using BenchmarkDotNet.Attributes;

namespace Benchmarks;

[MemoryDiagnoser]
public class Benchmarks
{
    private IConverter _dink;
    private IConverter _pdfLib;
    private IConverter _pdfLibSetupPages;

    [Params("sample.html", "large-sample.html")]
    public string FileName;

    private string _currentHtml;

    private IEnumerable<IConverter> Converters()
    {
        yield return _dink;
        yield return _pdfLib;
        yield return _pdfLibSetupPages;
    }
    
    [GlobalSetup]
    public void Setup()
    {
        _dink = new DinkPdf();
        _pdfLib = new PdfLib();
        _pdfLibSetupPages = new PdfLib_SetupPages();

        foreach(var converter in Converters()) converter.GlobalSetupAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        foreach(var converter in Converters()) converter.GlobalCleanupAsync().GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach(var converter in Converters()) converter.IterationCleanupAsync().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _currentHtml = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName));
        foreach(var converter in Converters()) converter.IterationSetupAsync(_currentHtml).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task Dink()
    {
        await _dink.ConvertAsync(_currentHtml);
    }

    [Benchmark]
    public async Task PdfLib()
    {
        await _pdfLib.ConvertAsync(_currentHtml);
    }

    [Benchmark]
    public async Task PdfLib_SetupPages()
    {
        await _pdfLibSetupPages.ConvertAsync(_currentHtml);
    }
}