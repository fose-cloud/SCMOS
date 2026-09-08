using System.Threading.RateLimiting;

namespace Scmos.Api.Ai;

/// <summary>Per API instance: 20 starts/minute and four in flight, no waiting queue.</summary>
public sealed class AiRunLimiter : IDisposable
{
    private readonly FixedWindowRateLimiter _starts = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true,
    });
    private readonly SemaphoreSlim _active = new(4, 4);

    public IDisposable? TryEnter()
    {
        if (!_active.Wait(0)) return null;
        using var lease = _starts.AttemptAcquire();
        if (lease.IsAcquired) return new Release(_active);
        _active.Release();
        return null;
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release(); }
    }
    public void Dispose() { _starts.Dispose(); _active.Dispose(); }
}
