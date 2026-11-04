using System.Collections.Concurrent;

namespace Tracon;

/// <summary>Remembers which keys a "warn once per key" log line was written for, up to a capacity.</summary>
/// <remarks>
/// <para>
/// A warning that is written once per tenant needs a set of the tenants it was written for, and the tenant comes
/// from a request: a set that only grows lets a caller grow it by one entry for every identifier it made up. The
/// set stops taking keys at its capacity. A key that does not fit is not named in the log again, and the caller
/// is told once, so the log says that names were left out instead of going quiet about them.
/// </para>
/// <para>
/// The capacity is a soft bound: callers that pass the check together can take the set a few entries over it.
/// </para>
/// </remarks>
/// <param name="capacity">The most keys the set remembers.</param>
internal sealed class WarnedKeySet(int capacity)
{
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);
    private int _fullReported;

    /// <summary>Gets how many keys the set remembers.</summary>
    internal int Count => _keys.Count;

    /// <summary>Gets the capacity the set was made with.</summary>
    internal int Capacity { get; } = capacity;

    /// <summary>Decides whether a warning is written for a key.</summary>
    /// <param name="key">The key the warning is about.</param>
    /// <returns>What the caller does.</returns>
    public WarnedKeyAdmission Admit(string key)
    {
        if (_keys.ContainsKey(key))
        {
            return WarnedKeyAdmission.Known;
        }

        if (_keys.Count >= Capacity)
        {
            return Interlocked.Exchange(ref _fullReported, 1) == 0
                ? WarnedKeyAdmission.FullFirstTime
                : WarnedKeyAdmission.Full;
        }

        return _keys.TryAdd(key, 0) ? WarnedKeyAdmission.New : WarnedKeyAdmission.Known;
    }
}

/// <summary>What a caller does about a key it asks <see cref="WarnedKeySet"/> about.</summary>
internal enum WarnedKeyAdmission
{
    /// <summary>The key is new: write the warning that names it.</summary>
    New,

    /// <summary>The key was warned about already: write nothing.</summary>
    Known,

    /// <summary>The set is full and this is the first key it turned away: write one line that says names are left out.</summary>
    FullFirstTime,

    /// <summary>The set is full and said so already: write nothing.</summary>
    Full,
}
