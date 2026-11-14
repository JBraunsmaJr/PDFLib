namespace PDFLib.Samples.WebApi.Diagnostics;

/// <summary>
/// Counts what the API has rendered. Lock-free, so it adds nothing measurable to a request.
/// </summary>
public sealed class RenderStats
{
    private long _completed;
    private long _failed;
    private long _rejected;
    private long _bytes;
    private int _inFlight;
    private int _peakInFlight;

    public long Completed => Interlocked.Read(ref _completed);
    public long Failed => Interlocked.Read(ref _failed);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long BytesWritten => Interlocked.Read(ref _bytes);
    public int InFlight => Volatile.Read(ref _inFlight);
    public int PeakInFlight => Volatile.Read(ref _peakInFlight);

    /// <summary>Marks the start of a render. Call <see cref="Completed"/> or <see cref="Fail"/> exactly once afterwards.</summary>
    public void Begin()
    {
        var now = Interlocked.Increment(ref _inFlight);
        int peak;
        while (now > (peak = Volatile.Read(ref _peakInFlight)))
            if (Interlocked.CompareExchange(ref _peakInFlight, now, peak) == peak)
                break;
    }

    public void Complete(long bytes)
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _completed);
        Interlocked.Add(ref _bytes, bytes);
    }

    public void Fail()
    {
        Interlocked.Decrement(ref _inFlight);
        Interlocked.Increment(ref _failed);
    }

    public void Reject() => Interlocked.Increment(ref _rejected);

    public void Reset()
    {
        Interlocked.Exchange(ref _completed, 0);
        Interlocked.Exchange(ref _failed, 0);
        Interlocked.Exchange(ref _rejected, 0);
        Interlocked.Exchange(ref _bytes, 0);
        Volatile.Write(ref _peakInFlight, Volatile.Read(ref _inFlight));
    }
}
