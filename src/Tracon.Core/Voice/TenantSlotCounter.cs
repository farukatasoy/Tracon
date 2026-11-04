using System.Collections.Concurrent;

namespace Tracon;

/// <summary>Counts the slots each tenant holds, up to a limit, and keeps an entry only while the tenant holds one.</summary>
/// <remarks>
/// <para>
/// A tenant that reserved a slot and gave it back used to leave a zero in the map for the life of the process. The
/// tenant identifier comes from a request, so an installation that accepts any identifier let a caller grow the map
/// by one entry for every identifier it made up. The entry now goes when the count reaches zero.
/// </para>
/// <para>
/// Both the reservation and the release are compare-and-swap loops. Two requests that read the count together
/// would both see a value under the limit and both pass; and a release that removes the zero must remove it only
/// while it still is zero, because a reservation may have taken the tenant to one between the decrement and the
/// removal.
/// </para>
/// </remarks>
internal sealed class TenantSlotCounter
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Gets how many tenants hold at least one slot.</summary>
    internal int TrackedTenants => _counts.Count;

    /// <summary>Reserves one slot for a tenant when it holds fewer than <paramref name="limit"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="limit">The most slots the tenant may hold.</param>
    /// <returns><see langword="true"/> when the slot was reserved.</returns>
    /// <remarks>A limit under one reserves nothing and leaves no entry behind.</remarks>
    public bool TryReserve(string tenantId, int limit)
    {
        while (true)
        {
            if (!_counts.TryGetValue(tenantId, out var current))
            {
                if (limit < 1)
                {
                    return false;
                }

                if (_counts.TryAdd(tenantId, 1))
                {
                    return true;
                }

                continue;
            }

            if (current >= limit)
            {
                return false;
            }

            if (_counts.TryUpdate(tenantId, current + 1, current))
            {
                return true;
            }
        }
    }

    /// <summary>Gives one slot of a tenant back. A tenant that holds none is left alone.</summary>
    /// <param name="tenantId">The tenant.</param>
    public void Release(string tenantId)
    {
        while (true)
        {
            if (!_counts.TryGetValue(tenantId, out var current) || current <= 0)
            {
                return;
            }

            if (!_counts.TryUpdate(tenantId, current - 1, current))
            {
                continue;
            }

            if (current == 1)
            {
                // Only while the value is still zero: a reservation that ran in between keeps its entry.
                _counts.TryRemove(new KeyValuePair<string, int>(tenantId, 0));
            }

            return;
        }
    }

    /// <summary>Gets how many slots a tenant holds.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <returns>The count; zero for a tenant that holds none.</returns>
    public int CountFor(string tenantId)
        => _counts.TryGetValue(tenantId, out var count) ? count : 0;
}
