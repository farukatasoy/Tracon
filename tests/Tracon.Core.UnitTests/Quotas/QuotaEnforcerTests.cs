using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>Tests of quota enforcement and consumption accounting.</summary>
public sealed class QuotaEnforcerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task Allowed_when_no_rule_exists()
    {
        var (enforcer, _, _) = Build();

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Allowed_when_below_the_limit()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 5);
        await RecordAsync(enforcer, clock, runs: 4);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Rejected_once_the_limit_is_reached()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 3);
        await RecordAsync(enforcer, clock, runs: 3);

        var decision = await enforcer.CheckAsync(Tenant, Agent);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Runs);
        decision.Limit.ShouldBe(3);
        decision.Used.ShouldBe(3);
        decision.ResetsAt.ShouldNotBeNull();
        decision.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Token_quota_is_enforced_separately()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxTokens: 1000);
        await RecordAsync(enforcer, clock, runs: 1, tokens: 1200);

        var decision = await enforcer.CheckAsync(Tenant, Agent);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Tokens);
    }

    [Fact]
    public async Task Run_with_no_defined_price_does_not_count_toward_the_cost_quota()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxCost: 1.0m);

        // Cost = null: the price is undefined. It is not added as zero either;
        // the cost quota never sees this run, so it is not exceeded.
        await RecordAsync(enforcer, clock, runs: 1, tokens: 5000, cost: null);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Cost_quota_is_enforced_when_a_price_is_defined()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxCost: 1.0m);
        await RecordAsync(enforcer, clock, runs: 1, cost: 1.5m);

        var decision = await enforcer.CheckAsync(Tenant, Agent);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Cost);
    }

    [Fact]
    public async Task Tenant_wide_rule_also_counts_another_agents_consumption()
    {
        var (enforcer, store, clock) = Build();

        // agentName = null: the rule applies to the whole tenant.
        await SaveQuotaAsync(store, agentName: null, maxRuns: 2);

        await RecordAsync(enforcer, clock, runs: 1, agentName: "billing");
        await RecordAsync(enforcer, clock, runs: 1, agentName: "support");

        // Two different agents, the same tenant-wide counter.
        (await enforcer.CheckAsync(Tenant, "anything")).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Agent_rule_does_not_affect_another_agent()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, agentName: "support", maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 1, agentName: "support");

        (await enforcer.CheckAsync(Tenant, "support")).IsAllowed.ShouldBeFalse();
        (await enforcer.CheckAsync(Tenant, "billing")).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_tenants_consumption_is_not_counted()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 1, tenantId: "other");

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Counter_resets_when_the_period_rolls_over()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 1);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeFalse();

        // The next day: a new period, a new counter. The old row is not
        // deleted, it is simply no longer queried.
        clock.Advance(TimeSpan.FromDays(1));

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Disabled_rule_is_not_enforced()
    {
        var (enforcer, store, clock) = Build();

        await SaveQuotaAsync(store, maxRuns: 1, enabled: false);
        await RecordAsync(enforcer, clock, runs: 5);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Nothing_is_rejected_while_quotas_are_disabled()
    {
        var (enforcer, store, clock) = Build(options => options.Enabled = false);

        await SaveQuotaAsync(store, maxRuns: 1);
        await RecordAsync(enforcer, clock, runs: 10);

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Event_is_published_when_the_threshold_is_exceeded()
    {
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        // 80% threshold: 8/10.
        await RecordAsync(enforcer, clock, runs: 8);

        publisher.Events.Count.ShouldBe(1);
        publisher.Events[0].EventType.ShouldBe(WebhookEvents.QuotaThreshold);
        publisher.Events[0].Payload.Quota.ShouldNotBeNull();
        publisher.Events[0].Payload.Quota!.ThresholdPercent.ShouldBe(80);
        publisher.Events[0].Payload.Quota!.Metric.ShouldBe(QuotaMetric.Runs);
    }

    [Fact]
    public async Task Same_threshold_is_published_once_per_period()
    {
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        await RecordAsync(enforcer, clock, runs: 8);   // 80% -> event
        await RecordAsync(enforcer, clock, runs: 1);   // 90% -> NO event

        publisher.Events.Count(e => e.Payload.Quota?.ThresholdPercent == 80).ShouldBe(1);
    }

    [Fact]
    public async Task Hundred_percent_threshold_is_published_separately()
    {
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        await RecordAsync(enforcer, clock, runs: 8);    // 80%
        await RecordAsync(enforcer, clock, runs: 2);    // 100%

        publisher.Events.Select(e => e.Payload.Quota!.ThresholdPercent)
            .ShouldBe([80, 100], ignoreOrder: true);
    }

    [Fact]
    public async Task Allowed_by_default_when_the_store_fails()
    {
        var enforcer = new QuotaEnforcer(
            new ThrowingQuotaStore(),
            Options(new TraconQuotaOptions()),
            timeProvider: new ManualTimeProvider());

        // A temporarily unreachable database must not stop the service.
        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Store_failure_is_rejected_in_strict_setup()
    {
        var enforcer = new QuotaEnforcer(
            new ThrowingQuotaStore(),
            Options(new TraconQuotaOptions { AllowOnStoreFailure = false }),
            timeProvider: new ManualTimeProvider());

        (await enforcer.CheckAsync(Tenant, Agent)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task No_exception_leaks_when_writing_consumption_fails()
    {
        var enforcer = new QuotaEnforcer(
            new ThrowingQuotaStore(),
            Options(new TraconQuotaOptions()),
            timeProvider: new ManualTimeProvider());

        // Observability must not break functionality: a completed run is not
        // retroactively broken because its counter could not be written.
        await Should.NotThrowAsync(() => enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = Tenant,
            AgentName = Agent,
            OccurredAt = DateTimeOffset.UtcNow,
        }).AsTask());
    }

    private static (QuotaEnforcer Enforcer, IQuotaStore Store, ManualTimeProvider Clock) Build(
        Action<TraconQuotaOptions>? configure = null,
        IWebhookPublisher? publisher = null)
    {
        var options = new TraconQuotaOptions();
        configure?.Invoke(options);

        var store = new InMemoryQuotaStore();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));

        return (new QuotaEnforcer(store, Options(options), publisher, clock), store, clock);
    }

    private static StaticOptionsMonitor<TraconQuotaOptions> Options(TraconQuotaOptions options)
        => new(options);

    private static async Task SaveQuotaAsync(
        IQuotaStore store,
        string? agentName = Agent,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null,
        bool enabled = true)
        => await store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = QuotaPeriod.Daily,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static async Task RecordAsync(
        QuotaEnforcer enforcer,
        ManualTimeProvider clock,
        long runs = 1,
        long tokens = 0,
        decimal? cost = null,
        string agentName = Agent,
        string tenantId = Tenant)
        => await enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = tenantId,
            AgentName = agentName,
            Runs = runs,
            Tokens = tokens,
            Cost = cost,
            OccurredAt = clock.GetUtcNow(),
        });

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        public List<(string EventType, WebhookEventPayload Payload)> Events { get; } = [];

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            Events.Add((eventType, payload));

            return new ValueTask<int>(1);
        }
    }

    private sealed class ThrowingQuotaStore : IQuotaStore
    {
        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unreachable");
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
