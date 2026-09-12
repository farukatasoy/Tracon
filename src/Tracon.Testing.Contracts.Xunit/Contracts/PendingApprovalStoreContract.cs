namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IPendingApprovalStore"/> contract.
/// </summary>
/// <remarks>
/// A pending approval request is a <strong>security record</strong>: one
/// tenant's operator cannot see, and must NOT be able to decide, another
/// tenant's approval. The <see cref="TryDeleteAsync"/> hook is wired to the
/// mutation operation here — <see cref="IPendingApprovalStore.DecideAsync"/> —
/// its meaning is "decide", not "delete", but the two-way isolation check
/// needs the same shape.
/// </remarks>
public abstract class PendingApprovalStoreContract : TenantIsolationContract<IPendingApprovalStore>
{
    /// <summary>
    /// Opens a run row at the given id before an approval request is written.
    /// </summary>
    /// <param name="runId">The run id.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// In the SQL implementations, <c>pending_approvals.run_id</c> is a
    /// foreign key into the <c>runs</c> table (the SAME pattern as
    /// <c>RunInputStoreContract.PrepareRunAsync</c>); the in-memory
    /// implementation has no such link, and the hook does nothing.
    /// </remarks>
    protected virtual ValueTask PrepareRunAsync(Guid runId, string tenantId) => default;

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var approval = await ApprovalAsync(tenantId, name);

        await Store.CreateAsync(approval);

        return approval.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return await Store.GetAsync((Guid)key) is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;

        return (await Store.ListPendingAsync()).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return await Store.DecideAsync((Guid)key, approved: true, "operator@example", Now);
    }

    [Fact]
    public async Task Created_request_is_read_back()
    {
        var approval = await ApprovalAsync("tenant-a", "cancel-order");

        await Store.CreateAsync(approval);

        AmbientTenant.TenantId = "tenant-a";

        var loaded = await Store.GetAsync(approval.Id);

        loaded.ShouldNotBeNull();
        loaded.ToolName.ShouldBe("cancel_order");
        loaded.Status.ShouldBe(ApprovalStatus.Pending);
        loaded.SessionId.ShouldBe(approval.SessionId);
    }

    [Fact]
    public async Task Presentation_round_trips_through_create_and_get()
    {
        var approval = (await ApprovalAsync("tenant-a", "cancel-order")) with
        {
            Presentation = new ToolApprovalPresentation
            {
                EntityType = "order",
                EntityId = "42",
                EntityName = "Order #42",
                Message = "Cancel order #42 for customer Jane Doe.",
            },
        };

        await Store.CreateAsync(approval);

        AmbientTenant.TenantId = "tenant-a";

        var loaded = await Store.GetAsync(approval.Id);

        loaded.ShouldNotBeNull();
        loaded.Presentation.ShouldNotBeNull();
        loaded.Presentation!.EntityType.ShouldBe("order");
        loaded.Presentation.EntityId.ShouldBe("42");
        loaded.Presentation.EntityName.ShouldBe("Order #42");
        loaded.Presentation.Message.ShouldBe("Cancel order #42 for customer Jane Doe.");
    }

    [Fact]
    public async Task Absent_presentation_is_read_back_as_null()
    {
        var approval = await ApprovalAsync("tenant-a", "cancel-order");

        approval.Presentation.ShouldBeNull();

        await Store.CreateAsync(approval);

        AmbientTenant.TenantId = "tenant-a";

        (await Store.GetAsync(approval.Id))!.Presentation.ShouldBeNull();
    }

    [Fact]
    public async Task Listing_returns_only_pending_requests()
    {
        AmbientTenant.TenantId = "tenant-a";

        var pending = await ApprovalAsync("tenant-a", "pending");
        var decided = await ApprovalAsync("tenant-a", "decided");

        await Store.CreateAsync(pending);
        await Store.CreateAsync(decided);
        await Store.DecideAsync(decided.Id, approved: true, "operator@example", Now);

        var listed = (await Store.ListPendingAsync()).ShouldHaveSingleItem();

        listed.Id.ShouldBe(pending.Id);
    }

    [Fact]
    public async Task Deciding_updates_status_and_actor()
    {
        AmbientTenant.TenantId = "tenant-a";

        var approval = await ApprovalAsync("tenant-a", "cancel-order");
        await Store.CreateAsync(approval);

        var decidedAt = Now;
        var applied = await Store.DecideAsync(approval.Id, approved: true, "operator@example", decidedAt);

        applied.ShouldBeTrue();

        var loaded = await Store.GetAsync(approval.Id);

        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(ApprovalStatus.Approved);
        loaded.DecidedBy.ShouldBe("operator@example");
        loaded.DecidedAt.ShouldBe(decidedAt);
    }

    [Fact]
    public async Task Second_decision_is_rejected()
    {
        AmbientTenant.TenantId = "tenant-a";

        var approval = await ApprovalAsync("tenant-a", "cancel-order");
        await Store.CreateAsync(approval);

        (await Store.DecideAsync(approval.Id, approved: true, "operator-1@example", Now)).ShouldBeTrue();
        (await Store.DecideAsync(approval.Id, approved: false, "operator-2@example", Now)).ShouldBeFalse();

        // The first decision is preserved; the second attempt does NOT overwrite it.
        (await Store.GetAsync(approval.Id))!.DecidedBy.ShouldBe("operator-1@example");
    }

    [Fact]
    public async Task Expired_request_is_closed_and_returned()
    {
        AmbientTenant.TenantId = "tenant-a";

        var expired = (await ApprovalAsync("tenant-a", "expired")) with { ExpiresAt = Now - TimeSpan.FromMinutes(1) };
        var fresh = (await ApprovalAsync("tenant-a", "fresh")) with { ExpiresAt = Now + TimeSpan.FromHours(1) };

        await Store.CreateAsync(expired);
        await Store.CreateAsync(fresh);

        var closed = (await Store.ExpireAsync(Now, max: 100)).ShouldHaveSingleItem();

        closed.Id.ShouldBe(expired.Id);
        closed.Status.ShouldBe(ApprovalStatus.Expired);

        (await Store.ListPendingAsync()).ShouldHaveSingleItem().Id.ShouldBe(fresh.Id);
    }

    [Fact]
    public async Task Expiry_scan_does_not_exceed_the_max_limit()
    {
        AmbientTenant.TenantId = "tenant-a";

        for (var i = 0; i < 3; i++)
        {
            var approval = (await ApprovalAsync("tenant-a", $"expired-{i}")) with
            {
                ExpiresAt = Now - TimeSpan.FromMinutes(1),
            };

            await Store.CreateAsync(approval);
        }

        (await Store.ExpireAsync(Now, max: 2)).Count.ShouldBe(2);
    }

    private static DateTimeOffset Now => new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private async ValueTask<PendingApproval> ApprovalAsync(string tenantId, string name)
    {
        var runId = Guid.NewGuid();

        await PrepareRunAsync(runId, tenantId);

        return new PendingApproval
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RunId = runId,
            SessionId = $"session-{name}",
            RequestId = $"request-{name}",
            ToolName = "cancel_order",
            Status = ApprovalStatus.Pending,
            ExpiresAt = Now + TimeSpan.FromHours(24),
            CreatedAt = Now,
        };
    }
}
