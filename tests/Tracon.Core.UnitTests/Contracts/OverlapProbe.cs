using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// Wraps a store and remembers the most calls that were inside it at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// A concurrent-writer case of a store contract only means something if its calls really overlap. The in-memory stores
/// answer at once, so calls started one after the other on one thread (<c>Task.WhenAll</c> over a store that completes
/// synchronously) never do, and a case written that way passed a store that looked and then wrote, 0 runs in 20. This probe
/// is the check that a case is not that: it counts the calls in flight, on entry and on return, and a case that never has two
/// in flight is not concurrent.
/// </para>
/// <para>
/// It watches the part of a call that runs on the calling thread, which is all of it for an in-memory store. A caller that
/// waits for the store's lock is inside the call, so two threads released together overlap for as long as one of them holds
/// the lock.
/// </para>
/// </remarks>
/// <typeparam name="T">The store interface.</typeparam>
public class OverlapProbe<T> : DispatchProxy
    where T : class
{
    private T? _inner;
    private int _inFlight;
    private int _highest;

    /// <summary>Gets the most calls that were inside the store at the same moment.</summary>
    public int MostInFlight => Volatile.Read(ref _highest);

    internal void Attach(T inner) => _inner = inner;

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var now = Interlocked.Increment(ref _inFlight);

        for (var seen = Volatile.Read(ref _highest); now > seen; seen = Volatile.Read(ref _highest))
        {
            if (Interlocked.CompareExchange(ref _highest, now, seen) == seen)
            {
                break;
            }
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();

            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>Builds <see cref="OverlapProbe{T}"/> wrappers.</summary>
public static class OverlapProbe
{
    /// <summary>Wraps a store.</summary>
    /// <typeparam name="T">The store interface.</typeparam>
    /// <param name="inner">The store.</param>
    /// <param name="probe">The probe that counts the calls.</param>
    /// <returns>The store to hand to the case.</returns>
    public static T Wrap<T>(T inner, out OverlapProbe<T> probe)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(inner);

        var proxy = DispatchProxy.Create<T, OverlapProbe<T>>();

        probe = (OverlapProbe<T>)(object)proxy;
        probe.Attach(inner);

        return proxy;
    }
}
