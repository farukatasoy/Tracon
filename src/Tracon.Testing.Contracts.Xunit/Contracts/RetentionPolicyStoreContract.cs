namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IRetentionPolicyStore"/> contract.
/// </summary>
public abstract class RetentionPolicyStoreContract : TenantIsolationContract<IRetentionPolicyStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var policy = await Store.SavePolicyAsync(Policy(tenantId), cancellationToken);

        var run = await Store.CreateRunAsync(Run() with { TenantId = tenantId }, cancellationToken);
        await Store.AppendRunProgressAsync(run.Id, deletedDelta: 1, archivedDelta: 0, cancellationToken);
        await Store.CompleteRunAsync(run.Id, DateTimeOffset.UtcNow, errorMessage: null, cancellationToken);

        return policy.Target;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var policy = await Store.GetPolicyAsync(tenantId, (string)key);

        // Run history also belongs to the tenant.
        var runs = await Store.ListRunsAsync(tenantId, (string)key, skip: 0, take: 50);
        (runs.Count > 0).ShouldBe(policy is not null);

        return policy is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListPoliciesAsync(tenantId, cancellationToken)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeletePolicyAsync(tenantId, (string)key);

    /// <inheritdoc />
    /// <remarks>
    /// Policy uniqueness is on the <c>(tenant, target)</c> pair; name is not
    /// a discriminator. The second tenant writes its own policy for the
    /// same target.
    /// </remarks>
    protected override async ValueTask<bool> TryOverwriteAsync(string tenantId, string name)
    {
        await SeedAsync(tenantId, name, CancellationToken.None);
        return true;
    }

    private const string Tenant = "test";

    [Fact]
    public async Task Saved_policy_is_read_back()
    {
        await Store.SavePolicyAsync(Policy(maxAgeDays: 30, archive: true));

        var loaded = await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents);

        loaded.ShouldNotBeNull();
        loaded.MaxAgeDays.ShouldBe(30);
        loaded.Archive.ShouldBeTrue();
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_the_same_target_a_second_time_overwrites_it()
    {
        await Store.SavePolicyAsync(Policy(maxAgeDays: 30));
        await Store.SavePolicyAsync(Policy(maxAgeDays: 7));

        var all = await Store.ListPoliciesAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].MaxAgeDays.ShouldBe(7);
    }

    [Fact]
    public async Task Falls_back_to_the_wildcard_policy_when_no_tenant_specific_record_exists()
    {
        await Store.SavePolicyAsync(Policy(tenantId: "*", maxAgeDays: 14));

        var resolved = await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.TenantId.ShouldBe("*");
        resolved.MaxAgeDays.ShouldBe(14);
    }

    [Fact]
    public async Task Tenant_specific_record_takes_precedence_over_the_wildcard()
    {
        await Store.SavePolicyAsync(Policy(tenantId: "*", maxAgeDays: 30));
        await Store.SavePolicyAsync(Policy(tenantId: Tenant, maxAgeDays: 7));

        var resolved = await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.TenantId.ShouldBe(Tenant);
        resolved.MaxAgeDays.ShouldBe(7);
    }

    [Fact]
    public async Task Another_tenants_policy_is_not_visible()
    {
        await Store.SavePolicyAsync(Policy(tenantId: Tenant));

        (await Store.GetPolicyAsync("other", RetentionTargets.RunEvents)).ShouldBeNull();
        (await Store.ListPoliciesAsync("other")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleted_policy_is_not_read_back()
    {
        await Store.SavePolicyAsync(Policy());

        (await Store.DeletePolicyAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeTrue();
        (await Store.GetPolicyAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeNull();
    }

    [Fact]
    public async Task Run_is_created_and_progress_accumulates()
    {
        var run = await Store.CreateRunAsync(Run());

        await Store.AppendRunProgressAsync(run.Id, deletedDelta: 100, archivedDelta: 40);
        await Store.AppendRunProgressAsync(run.Id, deletedDelta: 50, archivedDelta: 0);
        await Store.CompleteRunAsync(run.Id, DateTimeOffset.UtcNow, errorMessage: null);

        var history = await Store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);

        history.Count.ShouldBe(1);
        history[0].DeletedRows.ShouldBe(150);
        history[0].ArchivedRows.ShouldBe(40);
        history[0].CompletedAt.ShouldNotBeNull();
        history[0].Error.ShouldBeNull();
    }

    [Fact]
    public async Task Failed_run_carries_the_error_message()
    {
        var run = await Store.CreateRunAsync(Run());

        await Store.CompleteRunAsync(run.Id, DateTimeOffset.UtcNow, "connection dropped");

        var history = await Store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);

        history[0].Error.ShouldBe("connection dropped");
    }

    [Fact]
    public async Task Run_history_returns_newest_first()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.CreateRunAsync(Run(startedAt: now.AddMinutes(-10)));
        var latest = await Store.CreateRunAsync(Run(startedAt: now));

        var history = await Store.ListRunsAsync(Tenant, RetentionTargets.RunEvents, 0, 10);

        history[0].Id.ShouldBe(latest.Id);
    }

    private static RetentionPolicy Policy(
        string tenantId = Tenant,
        int? maxAgeDays = 30,
        bool archive = false)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = maxAgeDays,
            Archive = archive,
            Enabled = true,
            CreatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
        };

    private static RetentionRun Run(DateTimeOffset? startedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            StartedAt = startedAt ?? new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
        };
}
