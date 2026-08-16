using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Tests for the online evaluation window summary (Phase 49).</summary>
public sealed class OnlineEvalSummaryServiceTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Webhook_does_not_fire_before_the_minimum_sample_size_is_reached()
    {
        var (service, _, publisher, _) = Build(options => options.MinSampleSize = 5);

        for (var i = 0; i < 3; i++)
        {
            await service.RecordScoreAsync(Tenant, 10);
        }

        publisher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_fires_when_below_threshold_and_the_minimum_sample_size_is_exceeded()
    {
        var (service, _, publisher, _) = Build(options =>
        {
            options.MinSampleSize = 3;
            options.LowScoreThreshold = 60;
        });

        await service.RecordScoreAsync(Tenant, 10);
        await service.RecordScoreAsync(Tenant, 10);
        publisher.Events.ShouldBeEmpty();

        await service.RecordScoreAsync(Tenant, 10);

        publisher.Events.Count.ShouldBe(1);
        publisher.Events[0].EventType.ShouldBe(WebhookEvents.RunScoreLow);
        publisher.Events[0].Payload.Score.ShouldNotBeNull();
        publisher.Events[0].Payload.Score!.SampleCount.ShouldBe(3);
        publisher.Events[0].Payload.Score!.AverageScore.ShouldBe(10);
    }

    [Fact]
    public async Task Webhook_does_not_fire_when_the_average_is_above_the_threshold()
    {
        var (service, _, publisher, _) = Build(options =>
        {
            options.MinSampleSize = 1;
            options.LowScoreThreshold = 60;
        });

        await service.RecordScoreAsync(Tenant, 90);

        publisher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Samples_outside_the_window_are_dropped_from_the_summary()
    {
        var (service, _, _, clock) = Build(options => options.EvaluationWindow = TimeSpan.FromHours(1));

        await service.RecordScoreAsync(Tenant, 20);
        clock.Advance(TimeSpan.FromHours(2));

        var summary = await service.GetSummaryAsync(Tenant);

        summary.SampleCount.ShouldBe(0);
        summary.AverageScore.ShouldBeNull();
    }

    [Fact]
    public async Task Judge_cost_is_aggregated_from_judge_prefixed_eval_runs()
    {
        var (service, runs, _, now) = Build();
        var clock = now;

        await SeedJudgeRunAsync(runs, "judge:model", 0.05m, "USD", clock.GetUtcNow());
        await SeedJudgeRunAsync(runs, "judge:model", 0.03m, "USD", clock.GetUtcNow());

        // A different suite's case run (Phase 18) — does NOT carry the
        // "judge:" prefix; must not be mixed into judge cost.
        await SeedJudgeRunAsync(runs, "customer-support-agent", 100m, "USD", clock.GetUtcNow());

        var summary = await service.GetSummaryAsync(Tenant);

        summary.JudgeCost.ShouldBe(0.08m);
        summary.JudgeCostCurrency.ShouldBe("USD");
    }

    private static async Task SeedJudgeRunAsync(
        InMemoryRunStore runs,
        string agentName,
        decimal cost,
        string currency,
        DateTimeOffset now)
    {
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = agentName,
            Kind = RunKind.Eval,
            TenantId = Tenant,
            StartedAt = now,
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
            Cost = new RunCost { InputCost = cost, OutputCost = 0m, Currency = currency, Source = PricingSource.Catalog },
        });
    }

    private static (OnlineEvalSummaryService Service, InMemoryRunStore Runs, RecordingPublisher Publisher, ManualTimeProvider Clock) Build(
        Action<OnlineEvaluationOptions>? configure = null)
    {
        var options = new OnlineEvaluationOptions();
        configure?.Invoke(options);

        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var publisher = new RecordingPublisher();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));

        var service = new OnlineEvalSummaryService(
            runs,
            new StaticOptionsMonitor<OnlineEvaluationOptions>(options),
            publisher,
            clock);

        return (service, runs, publisher, clock);
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

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
}
