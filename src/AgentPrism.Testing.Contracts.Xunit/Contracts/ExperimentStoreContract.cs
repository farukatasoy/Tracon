namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IExperimentStore"/> contract.
/// </summary>
/// <remarks>
/// These tests run for <strong>every implementation</strong>. A behavior difference between
/// the in-memory store and the PostgreSQL store is a bug; this class catches that difference.
/// </remarks>
public abstract class ExperimentStoreContract : TenantIsolationContract<IExperimentStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(Experiment(name, tenantId));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    private const string TenantId = "default";

    [Fact]
    public async Task Save_and_get_round_trips()
    {
        var saved = await Store.SaveAsync(Experiment("d1"));

        var fetched = await Store.GetAsync(TenantId, "d1");

        fetched.ShouldNotBeNull();
        fetched.AgentName.ShouldBe(saved.AgentName);
        fetched.Status.ShouldBe(ExperimentStatus.Draft);
        fetched.Variants.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Draft_experiment_can_be_updated()
    {
        await Store.SaveAsync(Experiment("d1"));

        var updated = await Store.SaveAsync(Experiment("d1") with
        {
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 30 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 70 },
            ],
        });

        updated.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(70);
    }

    [Fact]
    public async Task Running_experiment_cannot_be_updated()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.SaveAsync(Experiment("d1")));
    }

    [Fact]
    public async Task Starting_returns_the_running_experiment()
    {
        await Store.SaveAsync(Experiment("d1"));

        var started = await Store.StartAsync(TenantId, "d1");

        started.Status.ShouldBe(ExperimentStatus.Running);
        started.StartedAt.ShouldNotBeNull();

        var running = await Store.GetRunningAsync(TenantId, "agent-a");
        running.ShouldNotBeNull();
        running!.Name.ShouldBe("d1");
    }

    [Fact]
    public async Task Second_start_for_same_agent_is_rejected()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Store.SaveAsync(Experiment("d2"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync(TenantId, "d2"));
    }

    [Fact]
    public async Task Stopping_stops_the_experiment_and_frees_the_running_slot()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var stopped = await Store.StopAsync(TenantId, "d1");

        stopped.Status.ShouldBe(ExperimentStatus.Stopped);
        stopped.EndedAt.ShouldNotBeNull();

        (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();

        // Since the slot is free, another experiment can be started on the same agent.
        await Store.SaveAsync(Experiment("d2"));
        var secondStart = await Store.StartAsync(TenantId, "d2");
        secondStart.Status.ShouldBe(ExperimentStatus.Running);
    }

    [Fact]
    public async Task Non_running_experiment_cannot_be_stopped()
    {
        await Store.SaveAsync(Experiment("d1"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StopAsync(TenantId, "d1"));
    }

    [Fact]
    public async Task Nonexistent_experiment_cannot_be_started()
        => await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync(TenantId, "missing"));

    [Fact]
    public async Task Running_experiment_cannot_be_deleted()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.DeleteAsync(TenantId, "d1"));
    }

    [Fact]
    public async Task Draft_experiment_can_be_deleted()
    {
        await Store.SaveAsync(Experiment("d1"));

        (await Store.DeleteAsync(TenantId, "d1")).ShouldBeTrue();
        (await Store.GetAsync(TenantId, "d1")).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_nonexistent_experiment_returns_false()
        => (await Store.DeleteAsync(TenantId, "missing")).ShouldBeFalse();

    [Fact]
    public async Task Listing_returns_only_that_tenant()
    {
        await Store.SaveAsync(Experiment("d1", tenantId: "tenant-a"));
        await Store.SaveAsync(Experiment("d2", tenantId: "tenant-b"));

        var listA = await Store.ListAsync("tenant-a");

        listA.ShouldHaveSingleItem().Name.ShouldBe("d1");
    }

    [Fact]
    public async Task Returns_null_when_no_experiment_is_running()
        => (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();

    [Fact]
    public async Task Canary_policy_is_set_and_read_back()
    {
        await Store.SaveAsync(Experiment("d1"));

        var updated = await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        updated.Canary.ShouldNotBeNull();
        updated.Canary!.CanaryVariant.ShouldBe("v2");
        updated.Canary.MaxErrorRateDelta.ShouldBe(0.1);

        var fetched = await Store.GetAsync(TenantId, "d1");
        fetched!.Canary.ShouldNotBeNull();
        fetched.Canary!.RampSteps.ShouldBe([5, 25, 50, 100]);
    }

    [Fact]
    public async Task Canary_policy_is_cleared_with_null()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        var cleared = await Store.SetCanaryPolicyAsync(TenantId, "d1", null);

        cleared.Canary.ShouldBeNull();
        (await Store.GetAsync(TenantId, "d1"))!.Canary.ShouldBeNull();
    }

    [Fact]
    public async Task Canary_policy_can_be_set_on_a_draft_experiment_too()
    {
        await Store.SaveAsync(Experiment("d1"));

        // 🚨 Unlike SaveAsync, SetCanaryPolicyAsync is NOT status-dependent.
        var updated = await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        updated.Status.ShouldBe(ExperimentStatus.Draft);
        updated.Canary.ShouldNotBeNull();
    }

    [Fact]
    public async Task Setting_canary_policy_is_EXEMPT_from_the_draft_only_edit_restriction()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        // SaveAsync is rejected while Running, but SetCanaryPolicyAsync is not.
        var updated = await Store.SetCanaryPolicyAsync(TenantId, "d1", CanaryPolicy());

        updated.Status.ShouldBe(ExperimentStatus.Running);
        updated.Canary.ShouldNotBeNull();
    }

    [Fact]
    public async Task Canary_ramp_updates_weights_on_a_running_experiment_and_stays_Running()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var advanced = await Store.AdvanceCanaryRampAsync(
            TenantId,
            "d1",
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 75 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 25 },
            ]);

        advanced.Status.ShouldBe(ExperimentStatus.Running);
        advanced.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(25);
    }

    [Fact]
    public async Task Canary_ramp_is_rejected_on_a_non_running_experiment()
    {
        await Store.SaveAsync(Experiment("d1"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.AdvanceCanaryRampAsync(
            TenantId,
            "d1",
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 75 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 25 },
            ]));
    }

    [Fact]
    public async Task Rollback_stops_the_experiment_restores_weights_and_records_the_reason()
    {
        await Store.SaveAsync(Experiment("d1"));
        await Store.StartAsync(TenantId, "d1");

        var rolledBack = await Store.RollbackCanaryAsync(
            TenantId,
            "d1",
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 100 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 0 },
            ],
            "error rate above threshold");

        rolledBack.Status.ShouldBe(ExperimentStatus.Stopped);
        rolledBack.EndedAt.ShouldNotBeNull();
        rolledBack.RollbackReason.ShouldBe("error rate above threshold");
        rolledBack.Variants.Single(static v => string.Equals(v.Name, "v2", StringComparison.Ordinal)).Weight.ShouldBe(0);

        (await Store.GetRunningAsync(TenantId, "agent-a")).ShouldBeNull();
    }

    [Fact]
    public async Task ListRunningWithCanaryAsync_returns_only_running_experiments_with_canary_defined()
    {
        // d1: Running without canary -- must NOT be in the list.
        await Store.SaveAsync(Experiment("d1", tenantId: "canary-a"));
        await Store.StartAsync("canary-a", "d1");

        // d2: Draft with canary -- must NOT be in the list (not Running).
        await Store.SaveAsync(Experiment("d2", tenantId: "canary-a"));
        await Store.SetCanaryPolicyAsync("canary-a", "d2", CanaryPolicy());

        // d3: canary AND Running -- must be in the list, even under a different tenant.
        await Store.SaveAsync(Experiment("d3", tenantId: "canary-b"));
        await Store.SetCanaryPolicyAsync("canary-b", "d3", CanaryPolicy());
        await Store.StartAsync("canary-b", "d3");

        var running = await Store.ListRunningWithCanaryAsync();

        running.ShouldContain(experiment => string.Equals(experiment.Name, "d3", StringComparison.Ordinal));
        running.ShouldNotContain(experiment => string.Equals(experiment.Name, "d1", StringComparison.Ordinal));
        running.ShouldNotContain(experiment => string.Equals(experiment.Name, "d2", StringComparison.Ordinal));
    }

    private static CanaryPolicy CanaryPolicy()
        => new()
        {
            CanaryVariant = "v2",
            MaxErrorRateDelta = 0.1,
            MinScore = 60,
            MinSampleSize = 20,
            RampSteps = [5, 25, 50, 100],
            RampInterval = TimeSpan.FromHours(1),
        };

    private static Experiment Experiment(string name, string tenantId = TenantId)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            AgentName = "agent-a",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 },
            ],
        };

    [Fact]
    public async Task Experiment_lifecycle_does_not_leak_across_tenants()
    {
        await Store.SaveAsync(Experiment("campaign", "tenant-a"));

        // Start, stop, and "find the running experiment" must all respect the same boundary.
        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StartAsync("tenant-b", "campaign"));

        await Store.StartAsync("tenant-a", "campaign");

        (await Store.GetRunningAsync("tenant-b", "agent-a")).ShouldBeNull();
        (await Store.GetRunningAsync("tenant-a", "agent-a")).ShouldNotBeNull();

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.StopAsync("tenant-b", "campaign"));

        await Store.StopAsync("tenant-a", "campaign");

        (await Store.GetRunningAsync("tenant-a", "agent-a")).ShouldBeNull();
    }
}
