using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Cevrimici degerlendirme pencere ozetinin testleri (Faz 49).</summary>
public sealed class OnlineEvalSummaryServiceTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Asgari_ornek_sayisina_ulasilmadan_webhook_tetiklenmez()
    {
        var (service, _, publisher, _) = Build(options => options.MinSampleSize = 5);

        for (var i = 0; i < 3; i++)
        {
            await service.RecordScoreAsync(Tenant, 10);
        }

        publisher.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Esik_altinda_ve_asgari_ornek_sayisi_asilinca_webhook_tetiklenir()
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
    public async Task Ortalama_esigin_ustundeyse_webhook_tetiklenmez()
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
    public async Task Pencere_disina_cikan_ornekler_ozetten_dusulur()
    {
        var (service, _, _, clock) = Build(options => options.EvaluationWindow = TimeSpan.FromHours(1));

        await service.RecordScoreAsync(Tenant, 20);
        clock.Advance(TimeSpan.FromHours(2));

        var summary = await service.GetSummaryAsync(Tenant);

        summary.SampleCount.ShouldBe(0);
        summary.AverageScore.ShouldBeNull();
    }

    [Fact]
    public async Task Yargic_maliyeti_judge_onekli_eval_calistirmalarindan_toplanir()
    {
        var (service, runs, _, now) = Build();
        var clock = now;

        await SeedJudgeRunAsync(runs, "judge:model", 0.05m, "USD", clock.GetUtcNow());
        await SeedJudgeRunAsync(runs, "judge:model", 0.03m, "USD", clock.GetUtcNow());

        // Farkli bir suite'in vaka calistirmasi (Faz 18) — "judge:" onekini
        // TASIMAZ; yargic maliyetine karismamalidir.
        await SeedJudgeRunAsync(runs, "musteri-destek-agent", 100m, "USD", clock.GetUtcNow());

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
