namespace Tracon;

/// <summary>A registered tenant.</summary>
/// <remarks>
/// A tenant registration is <strong>not required</strong>. The <c>tenant_id</c>
/// in other tables is the same text as this table's <c>slug</c> value, but it
/// carries no foreign key: a tenant with no registration must not error at
/// run time. The registration only provides a name and description to the
/// tenant picker in the UI.
/// </remarks>
public sealed record TenantDescriptor
{
    /// <summary>The tenant identifier. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The tenant key. The same text as the <c>tenant_id</c> column in other
    /// tables, and <c>ITenantContext.TenantId</c> returns this value.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>The name shown in the UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The store for tenant registrations.</summary>
public interface ITenantStore
{
    /// <summary>Lists registered tenants, ordered by key.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tenants.</returns>
    ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or updates a tenant. The key is <see cref="TenantDescriptor.Slug"/>.</summary>
    /// <param name="tenant">The tenant to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted record.</returns>
    ValueTask<TenantDescriptor> SaveAsync(TenantDescriptor tenant, CancellationToken cancellationToken = default);

    /// <summary>Deletes a tenant registration. The tenant's data is not deleted.</summary>
    /// <param name="slug">The tenant key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the record was deleted.</returns>
    ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default);
}
