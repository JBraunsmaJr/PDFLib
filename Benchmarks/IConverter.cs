namespace Benchmarks;

public interface IConverter
{
    void Convert(string html);
    void IterationSetup();
    void GlobalSetup();
    void IterationCleanup();
    void GlobalCleanup();
}