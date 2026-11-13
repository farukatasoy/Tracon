using Tracon.Testing.Contracts.Internal;

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

    /// <summary>
    /// Writers that save the same target at the same time leave one policy: the target is unique whoever wins.
    /// </summary>
    /// <remarks>
    /// Two nodes that apply one configuration, or an administrator console that saves twice, race on one target. A store
    /// that looks for the policy and then adds one lets both writers add, and the sweep reads two policies for one target.
    /// </remarks>
    [Fact]
    public async Task Writers_that_save_one_target_at_the_same_time_leave_one_policy()
    {
        // The writers start on threads of their own, released together, at a tenant of their own each round: saves started
        // one after the other on one thread never overlap, and a task that waits on a gate may be resumed one after the other
        // by the thread that opens it.
        const int Writers = 8;
        const int Rounds = 20;

        for (var round = 0; round < Rounds; round++)
        {
            var tenant = "racing-" + round;

            var policies = Enumerable.Range(0, Writers).Select(index => Policy(tenantId: tenant, maxAgeDays: index + 1)).ToArray();

            var saved = SimultaneousCalls.Run(
                Writers,
                index => Store.SavePolicyAsync(policies[index]).AsTask().GetAwaiter().GetResult());

            (await Store.ListPoliciesAsync(tenant)).Count.ShouldBe(1, $"round {round}: two policies hold one target");
            saved.Select(static policy => policy.Id).Distinct().Count().ShouldBe(1, $"round {round}: the writers were handed different policies");
        }
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
    public async Task Concurrent_progress_appends_all_count()
    {
        // The writers start on threads of their own, released together, and each appends many times to a run of its own each
        // round: appends started one after the other on one thread never overlap, and the window two overlapping appends
        // need is a few instructions wide in a store that reads the counts and writes them back.
        const int Writers = 8;
        const int PerWriter = 10;
        const int Rounds = 10;

        for (var round = 0; round < Rounds; round++)
        {
            var tenant = "progress-" + round;
            var run = await Store.CreateRunAsync(Run() with { TenantId = tenant });

            SimultaneousCalls.Run(
                Writers,
                _ =>
                {
                    for (var index = 0; index < PerWriter; index++)
                    {
                        Store.AppendRunProgressAsync(run.Id, deletedDelta: 1, archivedDelta: 2).AsTask().GetAwaiter().GetResult();
                    }
                });

            var history = await Store.ListRunsAsync(tenant, RetentionTargets.RunEvents, 0, 10);

            history.ShouldHaveSingleItem().DeletedRows.ShouldBe(Writers * PerWriter, $"round {round}: an append was lost");
            history[0].ArchivedRows.ShouldBe(Writers * PerWriter * 2, $"round {round}: an append was lost");
        }
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
