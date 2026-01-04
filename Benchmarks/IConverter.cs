namespace Benchmarks;

public interface IConverter
{
    Task ConvertAsync(string html);
    Task IterationSetupAsync(string html);
    Task GlobalSetupAsync();
    Task IterationCleanupAsync();
    Task GlobalCleanupAsync();
}