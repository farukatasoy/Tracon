namespace Tracon;

/// <summary>
/// The public, read-only face of <see cref="RetentionExecutor.PreviewAsync"/>: it names no tenant, it reads the
/// tenant of the flow it runs in.
/// </summary>
/// <remarks>
/// The executor's own entry point takes a tenant and stays internal, so a caller cannot ask about a tenant it is not
/// running as. The deleting entry point is not reachable from here at all: a run that deletes goes through the job
/// queue.
/// </remarks>
internal sealed class RetentionPreviewService(
    RetentionExecutor executor,
    ITenantContext tenants) : IRetentionPreviewService
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RetentionPreview>> PreviewAsync(
        string? target = null,
        CancellationToken cancellationToken = default)
    {
        // Refuses "*" and a character outside the tenant alphabet, so a context that cannot name a tenant fails here,
        // before any store sees the value.
        var tenantId = AmbientTenantScope.Normalize(tenants.TenantId);

        return executor.PreviewAsync(tenantId, target, cancellationToken);
    }
}
