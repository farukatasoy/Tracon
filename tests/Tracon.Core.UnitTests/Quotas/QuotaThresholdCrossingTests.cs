using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// <see cref="QuotaEnforcer.RecordAsync"/>'s <see cref="QuotaThresholdCrossing"/>
/// return value: empty unless <see cref="TraconQuotaOptions.PublishThresholdToRunStream"/>
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

    [Fact]
    public async Task Fired_threshold_entries_of_a_closed_period_are_released()
    {
        // The in-process fast path is keyed by period start, so every new day
        // adds a key. Nothing used to remove the old ones: a singleton enforcer
        // grew for the whole life of the process.
        var publisher = new RecordingPublisher();
        var (enforcer, store, clock) = Build(
            publisher: publisher,
            configure: static options =>
            {
                options.ThresholdPercents.Clear();
                options.ThresholdPercents.Add(100);
            });

        await SaveQuotaAsync(store, maxRuns: 1);
        await SaveQuotaAsync(store, agentName: null, maxRuns: 1, period: QuotaPeriod.Monthly);

        for (var day = 0; day < 5; day++)
        {
            if (day > 0)
            {
                clock.Advance(TimeSpan.FromDays(1));
            }

            await RecordAsync(enforcer, clock, runs: 1);
        }

        // Five daily crossings (one per day) and one monthly crossing.
        publisher.Events.Count.ShouldBe(6);

        // Only today's daily key and this month's monthly key are still open.
        enforcer.FiredThresholdCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_failed_durable_claim_is_retried_by_the_next_run_in_the_same_period()
    {
        var publisher = new RecordingPublisher();
        var store = new ClaimFaultQuotaStore(new InMemoryQuotaStore());
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));
        var enforcer = new QuotaEnforcer(store, Options(new TraconQuotaOptions()), publisher, clock);

        await SaveQuotaAsync(store, maxRuns: 10);

        store.BeforeNextClaim = static _ => throw new TraconException("simulated claim failure");

        // 80 %: the claim throws, RecordAsync swallows it, nothing is published.
        (await RecordAsync(enforcer, clock, runs: 8)).ShouldBeEmpty();
        publisher.Events.ShouldBeEmpty();

        // 90 %: the same 80 % threshold, same period. The failed claim must not
        // leave the key behind and silence the threshold until the period ends.
        await RecordAsync(enforcer, clock, runs: 1);

        publisher.Events.ShouldHaveSingleItem().EventType.ShouldBe(WebhookEvents.QuotaThreshold);
    }

    [Fact]
    public async Task A_cancelled_durable_claim_is_retried_by_the_next_run_in_the_same_period()
    {
        var publisher = new RecordingPublisher();
        var store = new ClaimFaultQuotaStore(new InMemoryQuotaStore());
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));
        var enforcer = new QuotaEnforcer(store, Options(new TraconQuotaOptions()), publisher, clock);

        await SaveQuotaAsync(store, maxRuns: 10);

        using var cancellation = new CancellationTokenSource();

        store.BeforeNextClaim = _ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        };

        // The caller's own cancellation comes out of RecordAsync.
        await Should.ThrowAsync<OperationCanceledException>(() => enforcer.RecordAsync(
            Consumption(clock, runs: 8),
            cancellation.Token).AsTask());

        publisher.Events.ShouldBeEmpty();

        await RecordAsync(enforcer, clock, runs: 1);

        publisher.Events.ShouldHaveSingleItem().EventType.ShouldBe(WebhookEvents.QuotaThreshold);
    }

    private static (QuotaEnforcer Enforcer, IQuotaStore Store, ManualTimeProvider Clock) Build(
        bool runStreamEnabled = false,
        Action<TraconQuotaOptions>? configure = null,
        IWebhookPublisher? publisher = null)
    {
        var options = new TraconQuotaOptions { PublishThresholdToRunStream = runStreamEnabled };
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
        QuotaPeriod period = QuotaPeriod.Daily)
        => await store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            AgentName = agentName,
            Period = period,
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
        => await enforcer.RecordAsync(Consumption(clock, runs, tokens, cost, agentName));

    private static QuotaConsumption Consumption(
        ManualTimeProvider clock,
        long runs = 1,
        long tokens = 0,
        decimal? cost = null,
        string agentName = Agent)
        => new()
        {
            TenantId = Tenant,
            AgentName = agentName,
            Runs = runs,
            Tokens = tokens,
            Cost = cost,
            OccurredAt = clock.GetUtcNow(),
        };

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

    /// <summary>
    /// Forwards to an inner store and runs a one-shot hook before the next
    /// durable threshold claim, so a test can make exactly one claim fail.
    /// </summary>
    private sealed class ClaimFaultQuotaStore(IQuotaStore inner) : IQuotaStore
    {
        public Action<CancellationToken>? BeforeNextClaim { get; set; }

        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, cancellationToken);

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => inner.SaveAsync(definition, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, id, cancellationToken);

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => inner.GetUsageAsync(query, cancellationToken);

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => inner.AddUsageAsync(consumption, periodStarts, cancellationToken);

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
        {
            var hook = BeforeNextClaim;
            BeforeNextClaim = null;
            hook?.Invoke(cancellationToken);

            return inner.TryClaimThresholdNotificationAsync(
                tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
