using System.Collections.Concurrent;

namespace Tracon;

/// <summary>Atomic read-modify-write for the in-memory stores.</summary>
internal static class ConcurrentDictionaryExtensions
{
    /// <summary>Replaces the value under <paramref name="key"/> with a change computed from the value it replaces.</summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type; an immutable record.</typeparam>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <param name="change">Builds the new value from the current one; <see langword="null"/> leaves the entry alone.</param>
    /// <returns><see langword="true"/> if the entry was replaced; <see langword="false"/> if it is gone or <paramref name="change"/> declined.</returns>
    /// <remarks>
    /// A read followed by an indexer write loses the update of a writer that lands in between: the
    /// second writer puts back a copy that predates the first writer's change. The write here is conditional
    /// on the value that was read, and a writer that lost the race reads again. <paramref name="change"/> may
    /// run more than once, so it must have no side effect.
    /// </remarks>
    public static bool TryReplace<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TValue, TValue?> change)
        where TKey : notnull
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(change);

        while (dictionary.TryGetValue(key, out var current))
        {
            if (change(current) is not { } replacement)
            {
                return false;
            }

            if (dictionary.TryUpdate(key, replacement, current))
            {
                return true;
            }
        }

        return false;
    }
}
