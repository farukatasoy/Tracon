namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IQuotaStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the PostgreSQL store must pass the same
/// scenarios. Two rules are especially critical: scope uniqueness
/// (<c>COALESCE(agent_name, '')</c>) and the atomicity of usage increments
/// (<c>ON CONFLICT DO UPDATE</c>).
/// </remarks>
public abstract class QuotaStoreContract : TenantIsolationContract<IQuotaStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var saved = await Store.SaveAsync(Quota(agentName: name) with { TenantId = tenantId });

        await Store.AddUsageAsync(Consumption() with { TenantId = tenantId, AgentName = name }, Periods);

        return saved.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var definition = await Store.GetAsync(tenantId, (Guid)key);

        // Counters are also locked to the same tenant; if the rule is not
        // visible, its usage must not be visible either.
        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = tenantId, AsOf = new DateTimeOffset(Today, TimeOnly.MinValue, TimeSpan.Zero) });

        (usage.Count > 0).ShouldBe(definition is not null);

        return definition is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (Guid)key);

    private const string Tenant = "test";

    private static readonly DateOnly Today = new(2026, 8, 3);

    private static readonly IReadOnlyDictionary<QuotaPeriod, DateOnly> Periods =
        new Dictionary<QuotaPeriod, DateOnly>
        {
            [QuotaPeriod.Daily] = Today,
            [QuotaPeriod.Monthly] = new(2026, 8, 1),
        };

    [Fact]
    public async Task Saved_rule_is_read_back()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 100, maxTokens: 5000, maxCost: 12.5m));

        var loaded = await Store.GetAsync(Tenant, saved.Id);

        loaded.ShouldNotBeNull();
        loaded.MaxRuns.ShouldBe(100);
        loaded.MaxTokens.ShouldBe(5000);
        loaded.MaxCost.ShouldBe(12.5m);
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Empty_limits_are_preserved_as_null()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 10));

        var loaded = await Store.GetAsync(Tenant, saved.Id);

        loaded.ShouldNotBeNull();
        loaded.MaxRuns.ShouldBe(10);
        loaded.MaxTokens.ShouldBeNull();
        loaded.MaxCost.ShouldBeNull();
    }

    [Fact]
    public async Task Saving_the_same_scope_a_second_time_overwrites_it()
    {
        // 🚨 In PostgreSQL, NULLs are not considered equal to each other; a
        // plain UNIQUE constraint would allow the same rule with a NULL
        // agent_name to be added an unlimited number of times. Uniqueness is
        // established via COALESCE(agent_name, '').
        await Store.SaveAsync(Quota(agentName: null, maxRuns: 10));
        await Store.SaveAsync(Quota(agentName: null, maxRuns: 20));

        var all = await Store.ListAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].MaxRuns.ShouldBe(20);
    }

    [Fact]
    public async Task Different_agent_becomes_a_separate_rule()
    {
        await Store.SaveAsync(Quota(agentName: null, maxRuns: 10));
        await Store.SaveAsync(Quota(agentName: "support", maxRuns: 5));

        (await Store.ListAsync(Tenant)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Different_period_becomes_a_separate_rule()
    {
        await Store.SaveAsync(Quota(period: QuotaPeriod.Daily, maxRuns: 10));
        await Store.SaveAsync(Quota(period: QuotaPeriod.Monthly, maxRuns: 200));

        (await Store.ListAsync(Tenant)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Another_tenants_rule_is_not_visible()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 10));

        (await Store.GetAsync("other", saved.Id)).ShouldBeNull();
        (await Store.ListAsync("other")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleted_rule_is_not_read_back()
    {
        var saved = await Store.SaveAsync(Quota(maxRuns: 10));

        (await Store.DeleteAsync(Tenant, saved.Id)).ShouldBeTrue();
        (await Store.GetAsync(Tenant, saved.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_nonexistent_rule_returns_false()
        => (await Store.DeleteAsync(Tenant, Guid.NewGuid())).ShouldBeFalse();

    [Fact]
    public async Task Usage_increments_both_the_agent_and_tenant_counters()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: 0.5m), Periods);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });

        // Two periods x two scopes (agent + tenant-wide) = four rows.
        usage.Count.ShouldBe(4);

        var agentDaily = Find(usage, "support", QuotaPeriod.Daily);
        agentDaily.Runs.ShouldBe(1);
        agentDaily.Tokens.ShouldBe(100);
        agentDaily.Cost.ShouldBe(0.5m);

        // Empty name = tenant-wide counter.
        var tenantDaily = Find(usage, string.Empty, QuotaPeriod.Daily);
        tenantDaily.Runs.ShouldBe(1);
        tenantDaily.Tokens.ShouldBe(100);
    }

    [Fact]
    public async Task Consecutive_usage_accumulates()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: 0.5m), Periods);
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 250, cost: 1.25m), Periods);
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 50, cost: 0.25m), Periods);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });
        var daily = Find(usage, "support", QuotaPeriod.Daily);

        daily.Runs.ShouldBe(3);
        daily.Tokens.ShouldBe(400);
        daily.Cost.ShouldBe(2.0m);
    }

    [Fact]
    public async Task Concurrent_increments_lose_no_increment()
    {
        // 🚨 ON CONFLICT DO UPDATE is atomic. If this were a read-modify-write
        // sequence, some of the runs finishing at the same time would be
        // silently swallowed.
        const int Concurrency = 20;

        await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ =>
            Store.AddUsageAsync(Consumption(runs: 1, tokens: 10), Periods).AsTask()));

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });
        var daily = Find(usage, "support", QuotaPeriod.Daily);

        daily.Runs.ShouldBe(Concurrency);
        daily.Tokens.ShouldBe(Concurrency * 10);
    }

    [Fact]
    public async Task Usage_with_undefined_price_does_not_break_the_cost_total()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: 2.0m), Periods);

        // Cost = null: price undefined. The total must not change; adding
        // NULL would turn the whole total into NULL.
        await Store.AddUsageAsync(Consumption(runs: 1, tokens: 100, cost: null), Periods);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });
        var daily = Find(usage, "support", QuotaPeriod.Daily);

        daily.Runs.ShouldBe(2);
        daily.Cost.ShouldBe(2.0m);
    }

    [Fact]
    public async Task Separate_period_keeps_a_separate_counter()
    {
        await Store.AddUsageAsync(Consumption(runs: 1), Periods);

        var nextDay = new Dictionary<QuotaPeriod, DateOnly>
        {
            [QuotaPeriod.Daily] = Today.AddDays(1),
            [QuotaPeriod.Monthly] = new(2026, 8, 1),
        };

        await Store.AddUsageAsync(Consumption(runs: 1), nextDay);

        var usage = await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });

        // The daily counter split, the monthly counter accumulated.
        Find(usage, "support", QuotaPeriod.Daily, Today).Runs.ShouldBe(1);
        Find(usage, "support", QuotaPeriod.Daily, Today.AddDays(1)).Runs.ShouldBe(1);
        Find(usage, "support", QuotaPeriod.Monthly).Runs.ShouldBe(2);
    }

    [Fact]
    public async Task Usage_is_filtered_by_agent_name()
    {
        await Store.AddUsageAsync(Consumption(runs: 1, agentName: "support"), Periods);
        await Store.AddUsageAsync(Consumption(runs: 1, agentName: "billing"), Periods);

        var filtered = await Store.GetUsageAsync(
            new QuotaUsageQuery { TenantId = Tenant, AgentName = "support" });

        filtered.ShouldAllBe(record => record.AgentName == "support");
        filtered.Count.ShouldBe(2);  // daily + monthly
    }

    [Fact]
    public async Task Usage_is_filtered_by_period()
    {
        await Store.AddUsageAsync(Consumption(runs: 1), Periods);

        var filtered = await Store.GetUsageAsync(
            new QuotaUsageQuery { TenantId = Tenant, Period = QuotaPeriod.Daily });

        filtered.ShouldAllBe(record => record.Period == QuotaPeriod.Daily);
    }

    [Fact]
    public async Task Another_tenants_usage_is_not_visible()
    {
        await Store.AddUsageAsync(Consumption(runs: 1), Periods);

        (await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = "other" })).ShouldBeEmpty();
    }

    private static QuotaUsageRecord Find(
        IReadOnlyList<QuotaUsageRecord> usage,
        string agentName,
        QuotaPeriod period,
        DateOnly? periodStart = null)
        => usage.First(record =>
            string.Equals(record.AgentName, agentName, StringComparison.Ordinal)
            && record.Period == period
            && (periodStart is null || record.PeriodStart == periodStart));

    private static QuotaDefinition Quota(
        string? agentName = "support",
        QuotaPeriod period = QuotaPeriod.Daily,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = period,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = true,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
        };

    private static QuotaConsumption Consumption(
        long runs = 1,
        long tokens = 0,
        decimal? cost = null,
        string agentName = "support")
        => new()
        {
            TenantId = Tenant,
            AgentName = agentName,
            Runs = runs,
            Tokens = tokens,
            Cost = cost,
            OccurredAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
        };
}
