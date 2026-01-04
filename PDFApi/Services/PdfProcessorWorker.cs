using PDFApi.Models;

namespace PDFApi.Services;

public class PdfProcessorWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReportQueue _queue;
    private readonly ILogger<PdfProcessorWorker> _logger;

    private readonly SemaphoreSlim _concurrencyLimiter = new(3);

    public PdfProcessorWorker(IServiceProvider serviceProvider, IReportQueue queue, ILogger<PdfProcessorWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.DequeueAsync(stoppingToken))
        {
            await _concurrencyLimiter.WaitAsync(stoppingToken);

            _ = ProcessJobTask(job).ContinueWith(_ => _concurrencyLimiter.Release(), stoppingToken);
        }
    }

    private async Task ProcessJobTask(PdfJobRequest job)
    {
        using var scope = _serviceProvider.CreateScope();

        var browserService = scope.ServiceProvider.GetRequiredService<IBrowsingService>();
        var storageService = scope.ServiceProvider.GetRequiredService<ISecureStorageService>();

        try
        {
            await using var pdfStream = await browserService.RenderHtmlToPdfStreamAsync(job.HtmlContent);

            var (path, key, iv) = await storageService.SaveEncryptedAsync(pdfStream);

            _logger.LogInformation("Job {JobId} Complete. Saved to {Path}", job.JobId, path);
        }
        catch (Exception ex)
        {
            _logger.LogError("Job {JobId} Failed. {Message}", job.JobId, ex.Message);
        }
    }
}