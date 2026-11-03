namespace Tracon;

/// <summary>
/// A fixed number of job slots whose semaphore outlives every slot still held.
/// </summary>
/// <remarks>
/// <para>
/// A job returns its slot from a task nobody awaits, and a job leased again while its previous
/// attempt still finishes holds a slot before it runs. A pool that disposed its semaphore when
/// the worker's loop ended therefore met a <c>Release</c> on a disposed semaphore, and the
/// exception sat in a task nobody observed. Here <see cref="Dispose"/> only stops the pool from
/// handing out slots: the semaphore is disposed by whoever returns the last slot, and
/// <see cref="Drained"/> completes at that moment, so a worker that waits for it knows no job
/// still holds one.
/// </para>
/// <para>
/// Every <see cref="TryAcquireAsync"/> that returns <see langword="true"/> must be answered by
/// exactly one <see cref="Release"/>, on every path: a slot that is never returned keeps
/// <see cref="Drained"/> from completing.
/// </para>
/// </remarks>
/// <param name="size">The number of slots.</param>
internal sealed class JobSlotPool(int size) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(size, size);
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();

    // Slots held plus acquisitions in progress; read and written under _gate.
    private int _users;
    private bool _closed;
    private bool _finished;

    /// <summary>Gets a task that completes once the pool is closed and every slot it handed out has come back.</summary>
    public Task Drained => _drained.Task;

    /// <summary>Takes a slot if one is free, without waiting for one.</summary>
    /// <param name="cancellationToken">Stops the attempt; a token that is already canceled throws.</param>
    /// <returns><see langword="true"/> when the caller now holds a slot; <see langword="false"/> when none is free or the pool is closed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            _users++;
        }

        try
        {
            if (await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }
        catch
        {
            Leave();
            throw;
        }

        Leave();

        return false;
    }

    /// <summary>Gives a slot back. Safe at any time after the slot was taken, the pool's closing included.</summary>
    public void Release()
    {
        _semaphore.Release();
        Leave();
    }

    /// <summary>
    /// Stops the pool from handing out slots. Slots already out stay valid until they are returned,
    /// and the pool's semaphore is disposed with the last of them.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
        }

        FinishIfIdle();
    }

    private void Leave()
    {
        lock (_gate)
        {
            _users--;
        }

        FinishIfIdle();
    }

    private void FinishIfIdle()
    {
        lock (_gate)
        {
            if (!_closed || _users > 0 || _finished)
            {
                return;
            }

            _finished = true;
        }

        _semaphore.Dispose();
        _drained.TrySetResult();
    }
}
