namespace Tracon;

/// <summary>
/// A fixed number of slots whose semaphore outlives every slot still held.
/// </summary>
/// <remarks>
/// <para>
/// Work that holds a slot ends on its own schedule: a job returns its slot from a task nobody awaits, a workflow run
/// returns its slot in the <c>finally</c> of an iterator its caller may still be draining when the host stops. An owner
/// that disposed a plain <see cref="SemaphoreSlim"/> with itself therefore met a <c>Release</c> on a disposed
/// semaphore, and the exception either sat in a task nobody observed or replaced the closing work of the
/// <c>finally</c> it was thrown from. Here <see cref="Dispose"/> only stops the pool from handing out slots: the
/// semaphore is disposed by whoever returns the last slot, and <see cref="Drained"/> completes at that moment, so an
/// owner that waits for it knows no work still holds one.
/// </para>
/// <para>
/// Every <see cref="TryAcquireAsync"/> that returns <see langword="true"/> and every <see cref="AcquireAsync"/> that
/// returns must be answered by exactly one <see cref="Release"/>, on every path: a slot that is never returned keeps
/// <see cref="Drained"/> from completing.
/// </para>
/// </remarks>
/// <param name="size">The number of slots.</param>
internal sealed class SlotPool(int size) : IDisposable
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

    /// <summary>Gets whether every slot is out now. A reading, not a reservation.</summary>
    public bool IsFull => _semaphore.CurrentCount == 0;

    /// <summary>Takes a slot if one is free, without waiting for one.</summary>
    /// <param name="cancellationToken">Stops the attempt; a token that is already canceled throws.</param>
    /// <returns><see langword="true"/> when the caller now holds a slot; <see langword="false"/> when none is free or the pool is closed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!Enter())
        {
            return false;
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

    /// <summary>Waits for a slot and takes it.</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>The completion task; the caller holds a slot when it completes.</returns>
    /// <exception cref="ObjectDisposedException">The pool is closed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled while waiting.</exception>
    public async ValueTask AcquireAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(!Enter(), this);

        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave();
            throw;
        }
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

    private bool Enter()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            _users++;

            return true;
        }
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
