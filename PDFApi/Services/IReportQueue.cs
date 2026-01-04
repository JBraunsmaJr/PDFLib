using PDFApi.Models;

namespace PDFApi.Services;

public interface IReportQueue
{
    ValueTask EnqueueJobAsync(PdfJobRequest job);
    IAsyncEnumerable<PdfJobRequest> DequeueAsync(CancellationToken cancellationToken);
}