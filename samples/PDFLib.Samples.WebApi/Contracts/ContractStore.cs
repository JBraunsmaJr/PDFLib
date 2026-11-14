using System.Collections.Concurrent;

namespace PDFLib.Samples.WebApi.Contracts;

/// <summary>
/// Keeps signed agreements between requests so they can be countersigned later.
/// </summary>
/// <remarks>
/// In memory and bounded, which is all a sample needs: the oldest documents are dropped once the limit is
/// reached, so a load test can create agreements indefinitely without the store growing. A real service
/// would keep the PDF bytes in blob storage or a database; nothing else about the flow changes, because
/// signing later needs only those bytes.
/// </remarks>
public sealed class ContractStore
{
    private const int Capacity = 200;
    private readonly ConcurrentDictionary<string, byte[]> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _order = new();

    public void Save(string id, byte[] pdf)
    {
        if (_documents.TryAdd(id, pdf))
        {
            _order.Enqueue(id);
            while (_documents.Count > Capacity && _order.TryDequeue(out var oldest))
                _documents.TryRemove(oldest, out _);
        }
        else
        {
            _documents[id] = pdf;
        }
    }

    public bool TryGet(string id, out byte[] pdf) => _documents.TryGetValue(id, out pdf!);
}
