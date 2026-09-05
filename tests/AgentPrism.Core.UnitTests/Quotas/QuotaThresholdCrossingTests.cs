using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// <see cref="QuotaEnforcer.RecordAsync"/>'s <see cref="QuotaThresholdCrossing"/>
/// return value: empty unless <see cref="AgentPrismQuotaOptions.PublishThresholdToRunStream"/>
/// is on, one entry per metric/threshold pair, and dedup within a single process.
/// </summary>
public sealed class QuotaThresholdCrossingTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task No_crossing_when_no_quota_rule_exists()
    {
        var (enforcer, _, clock) = Build(runStreamEnabled: true);

        var crossings = await RecordAsync(enforcer, clock, runs: 100);

        crossings.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_crossing_when_ThresholdPercents_is_empty()
    {
        var (enforcer, store, clock) = Build(runStreamEnabled: true, configure: options => options.ThresholdPercents.Clear());

        await SaveQuotaAsync(store, maxRuns: 10);

        var crossings = await RecordAsync(enforcer, clock, runs: 10);

        crossings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Crossing_is_empty_by_default_even_though_the_webhook_still_fires()
    {
        // K1 (146.5): PublishThresholdToRunStream defaults off. The webhook is
        // unaffected by the flag -- only the run-stream-facing return value is gated.
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(publisher: publisher);

        await SaveQuotaAsync(store, maxRuns: 10);

        var crossings = await RecordAsync(enforcer, clock, runs: 8);

        crossings.ShouldBeEmpty();
        publisher.Events.Count.ShouldBe(1);
        publisher.Events[0].EventType.ShouldBe(WebhookEvents.QuotaThreshold);
    }

    [Fact]
    public async Task Crossing_is_returned_when_run_stream_publishing_is_enabled()
    {
        var (enforcer, store, clock) = Build(runStreamEnabled: true);

        await SaveQuotaAsync(store, maxRuns: 10);

        var crossings = await RecordAsync(enforcer, clock, runs: 8);

        crossings.Count.ShouldBe(1);
        crossings[0].NoticeId.ShouldNotBeNullOrWhiteSpace();
        crossings[0].Metric.ShouldBe(QuotaMetric.Runs);
        crossings[0].Period.ShouldBe(QuotaPeriod.Daily);
        crossings[0].ThresholdPercent.ShouldBe(80);
        crossings[0].Limit.ShouldBe(10);
        crossings[0].Used.ShouldBe(8);
        crossings[0].AgentName.ShouldBe(Agent);
        crossings[0].ResetsAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Two_metrics_crossed_at_once_produce_two_separate_crossings()
    {
        var (enforcer, store, clock) = Build(runStreamEnabled: true);

        await SaveQuotaAsync(store, maxRuns: 10, maxTokens: 100);

        // A single consumption pushes BOTH the run counter and the token
        // counter past 80% at once.
        var crossings = await RecordAsync(enforcer, clock, runs: 8, tokens: 80);

        crossings.Count.ShouldBe(2);
        crossings.Select(c => c.Metric).ShouldBe([QuotaMetric.Runs, QuotaMetric.Tokens], ignoreOrder: true);
        crossings.Select(c => c.NoticeId).ToHashSet(StringComparer.Ordinal).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_threshold_already_crossed_is_not_returned_again_in_the_same_period()
    {
        var (enforcer, store, clock) = Build(runStreamEnabled: true);

        await SaveQuotaAsync(store, maxRuns: 10);

        var first = await RecordAsync(enforcer, clock, runs: 8);   // 80% -> crossing
        var second = await RecordAsync(enforcer, clock, runs: 1);  // 90% -> no NEW crossing

        first.Count.ShouldBe(1);
        second.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoticeId_is_stable_for_a_dedup_key_but_fresh_per_crossing()
    {
        var (enforcer, store, clock) = Build(runStreamEnabled: true);

        await SaveQuotaAsync(store, maxRuns: 10);

        var eighty = await RecordAsync(enforcer, clock, runs: 8);
        var hundred = await RecordAsync(enforcer, clock, runs: 2);

        string.Equals(eighty[0].NoticeId, hundred[0].NoticeId, StringComparison.Ordinal).ShouldBeFalse();
    }

    private static (QuotaEnforcer Enforcer, IQuotaStore Store, ManualTimeProvider Clock) Build(
        bool runStreamEnabled = false,
        Action<AgentPrismQuotaOptions>? configure = null,
        IWebhookPublisher? publisher = null)
    {
        var options = new AgentPrismQuotaOptions { PublishThresholdToRunStream = runStreamEnabled };
        configure?.Invoke(options);

        var store = new InMemoryQuotaStore();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));

        return (new QuotaEnforcer(store, Options(options), publisher, clock), store, clock);
    }

    private static StaticOptionsMonitor<AgentPrismQuotaOptions> Options(AgentPrismQuotaOptions options)
        => new(options);

    private static async Task SaveQuotaAsync(
        IQuotaStore store,
        string? agentName = Agent,
        long? maxRuns = null,
        long? maxTokens = null,
        decimal? maxCost = null)
        => await store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = QuotaPeriod.Daily,
            MaxRuns = maxRuns,
            MaxTokens = maxTokens,
            MaxCost = maxCost,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static async Task<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(
        QuotaEnforcer enforcer,
        ManualTimeProvider clock,
        long runs = 1,
        long tokens = 0,
        decimal? cost = null,
        string agentName = Agent)
        => await enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = Tenant,
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

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
