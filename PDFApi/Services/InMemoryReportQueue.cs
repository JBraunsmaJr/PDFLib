using System.Threading.Channels;
using PDFApi.Models;

namespace PDFApi.Services;

public class InMemoryReportQueue : IReportQueue
{
    private readonly Channel<PdfJobRequest> _channel;

    public InMemoryReportQueue()
    {
        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _channel = Channel.CreateBounded<PdfJobRequest>(options);
    }

    public async ValueTask EnqueueJobAsync(PdfJobRequest job)
    {
        await _channel.Writer.WriteAsync(job);
    }

    public IAsyncEnumerable<PdfJobRequest> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}