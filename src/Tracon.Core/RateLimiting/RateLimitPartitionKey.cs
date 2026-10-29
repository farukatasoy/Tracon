namespace Tracon;

/// <summary>
/// The key one rate-limit partition is counted under in an <see cref="IRateLimitCounterStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The key carries the tenant when the limit is partitioned by tenant, so it is typed:
/// it is built here and nowhere else, and the counter store receives only its text. A store
/// that builds its own key from a tenant and a group would be one forgotten segment away from
/// letting one tenant's window cut another's.
/// </para>
/// <para>
/// Forms: <c>endpoint:{group}:tenant:{tenant}</c>, <c>endpoint:{group}:global</c> and
/// <c>trigger:{triggerId}</c>. The group is one of the fixed names in
/// <see cref="RateLimitGroups"/> (they hold no <c>:</c>), the tenant is the last segment, in the
/// canonical lower-case form <see cref="AmbientTenantScope.Normalize"/> returns, and the trigger id
/// is a GUID in the <c>D</c> format. Two different partitions
/// therefore never share a key, whatever text a tenant identifier holds. The key holds no control
/// character.
/// </para>
/// </remarks>
// K-525: a key that carries a tenant is typed; no caller joins the segments by hand.
internal readonly record struct RateLimitPartitionKey
{
    private RateLimitPartitionKey(string value)
    {
        Value = value;
    }

    /// <summary>Gets the key text the counter store receives.</summary>
    public string Value { get; }

    /// <summary>Builds the key of a tenant's window on an endpoint group.</summary>
    /// <param name="group">The endpoint group, one of <see cref="RateLimitGroups"/>.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The key.</returns>
    public static RateLimitPartitionKey ForTenant(string group, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // The tenant identifier is matched case-insensitively everywhere else (AmbientTenantScope.Normalize);
        // a key that kept the caller's letter case would give "Acme" and "acme" two windows on a
        // case-sensitive store and one on SQL Server, and two windows double the limit.
        return new RateLimitPartitionKey($"endpoint:{group}:tenant:{AmbientTenantScope.Normalize(tenantId)}");
    }

    /// <summary>Builds the key of the one window every caller of an endpoint group shares.</summary>
    /// <param name="group">The endpoint group, one of <see cref="RateLimitGroups"/>.</param>
    /// <returns>The key.</returns>
    public static RateLimitPartitionKey ForGlobal(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);

        return new RateLimitPartitionKey($"endpoint:{group}:global");
    }

    /// <summary>Builds the key of an inbound trigger's window.</summary>
    /// <param name="triggerId">The trigger identifier.</param>
    /// <returns>The key.</returns>
    public static RateLimitPartitionKey ForTrigger(Guid triggerId)
        => new(string.Concat("trigger:", triggerId.ToString("D", System.Globalization.CultureInfo.InvariantCulture)));

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>The fixed names of the endpoint groups that keep a rate-limit counter of their own.</summary>
internal static class RateLimitGroups
{
    /// <summary>The management API group (<c>MapTracon</c>).</summary>
    public const string Management = "tracon";

    /// <summary>The AG-UI group (<c>MapTraconAGUI</c>). It does not spend the management group's window.</summary>
    public const string AgUi = "agui";
}
