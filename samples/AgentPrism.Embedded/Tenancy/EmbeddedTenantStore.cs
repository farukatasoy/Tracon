using System.Collections.Concurrent;

namespace AgentPrism.Embedded;

/// <summary>
/// 🚨 <strong>DEMONSTRATION ONLY.</strong> An in-memory stand-in for the host
/// application's own tenant directory.
/// </summary>
/// <remarks>
/// A real embedded application usually already has its own table of
/// organizations or accounts and binds this contract to read and write there
/// directly, so <c>/api/tenants</c> reflects the same directory the rest of the
/// host uses — instead of AgentPrism keeping a second, separate list.
/// </remarks>
internal sealed class EmbeddedTenantStore : ITenantStore
{
    private readonly ConcurrentDictionary<string, TenantDescriptor> _tenants = new(StringComparer.Ordinal);

    public EmbeddedTenantStore()
    {
        Seed("acme", "Acme Corp");
        Seed("globex", "Globex Inc");
    }

    public ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => new(_tenants.Values.OrderBy(static tenant => tenant.Slug, StringComparer.Ordinal).ToList());

    public ValueTask<TenantDescriptor> SaveAsync(TenantDescriptor tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        _tenants[tenant.Slug] = tenant;

        return new ValueTask<TenantDescriptor>(tenant);
    }

    public ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
        => new(_tenants.TryRemove(slug, out _));

    private void Seed(string slug, string displayName)
        => _tenants[slug] = new TenantDescriptor
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
