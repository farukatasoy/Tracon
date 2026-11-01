namespace Tracon.Core.UnitTests.Fakes;

/// <summary>A clock that never fires a timer and counts the ones created and disposed.</summary>
internal sealed class CountingTimeProvider : TimeProvider
{
    private int _created;
    private int _disposed;

    public int CreatedTimers => Volatile.Read(ref _created);

    public int RunningTimers => Volatile.Read(ref _created) - Volatile.Read(ref _disposed);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _created);

        return new CountedTimer(this);
    }

    private sealed class CountedTimer(CountingTimeProvider owner) : ITimer
    {
        private int _disposedOnce;

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedOnce, 1) == 0)
            {
                Interlocked.Increment(ref owner._disposed);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return default;
        }
    }
}
