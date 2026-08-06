namespace AgentPrism.Core.UnitTests.Storage;

/// <summary>
/// <see cref="RunStatistics.ScoredRuns"/> ve <see cref="RunStatistics.PositiveRate"/>
/// hesabinin testleri (Faz 31).
/// </summary>
/// <remarks>
/// <see cref="InMemoryRunStore"/>, ozet hesabinda kurucuya verilen
/// <see cref="IRunScoreStore"/>'u kullanir; bu testler ayni orneği paylasarak
/// puan yazma ile istatistik okumanin AYNI depoyu gordugunu dogrular.
/// </remarks>
public sealed class RunScoreStatisticsTests
{
    private const string Tenant = "test";

    [Fact]
    public async Task Puanli_calistirma_ScoredRuns_sayisina_girer_ve_oran_hesaplanir()
    {
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        var scoredRunId = AgentPrismId.NewId();
        var unscoredRunId = AgentPrismId.NewId();

        await runs.StartRunAsync(Start(scoredRunId));
        await runs.StartRunAsync(Start(unscoredRunId));

        // Iki farkli yazar ayni calistirmayi puanlar: 1 olumlu, 1 olumsuz.
        await scores.UpsertAsync(Score(scoredRunId, "alice", 1));
        await scores.UpsertAsync(Score(scoredRunId, "bob", 0));

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBe(0.5);
    }

    [Fact]
    public async Task Eval_calistirmasinin_puani_ozete_HIC_girmez()
    {
        // K-141'in ayrimi (RunKind.Eval haric tutulur) puanlar icin de gecerlidir.
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        var evalRunId = AgentPrismId.NewId();
        await runs.StartRunAsync(Start(evalRunId) with { Kind = RunKind.Eval });
        await scores.UpsertAsync(Score(evalRunId, "alice", 1));

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.TotalRuns.ShouldBe(0);
        stats.ScoredRuns.ShouldBe(0);
        stats.PositiveRate.ShouldBeNull();
    }

    [Fact]
    public async Task Yildiz_puani_ScoredRuns_sayilir_ama_orana_KATILMAZ()
    {
        // Yildiz ve ikili puanin ortalamasi anlamsizdir; oran yalniz Binary
        // uzerinden hesaplanir (bkz. docs/31-GERI-BILDIRIM-VE-PUANLAMA.md, riskler).
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        var runId = AgentPrismId.NewId();
        await runs.StartRunAsync(Start(runId));
        await scores.UpsertAsync(Score(runId, "alice", 5) with { Kind = RunScoreKind.Stars });

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(1);
        stats.PositiveRate.ShouldBeNull();
    }

    [Fact]
    public async Task Hic_puan_yoksa_ScoredRuns_sifir_ve_oran_null()
    {
        var scores = new InMemoryRunScoreStore();
        var runs = new InMemoryRunStore(scores);

        await runs.StartRunAsync(Start(AgentPrismId.NewId()));

        var stats = await runs.GetStatisticsAsync(new RunStatisticsQuery { TenantId = Tenant });

        stats.ScoredRuns.ShouldBe(0);
        stats.PositiveRate.ShouldBeNull();
    }

    private static RunStartInfo Start(Guid runId)
        => new()
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
        };

    private static RunScore Score(Guid runId, string author, int value)
        => new()
        {
            TenantId = Tenant,
            RunId = runId,
            Kind = RunScoreKind.Binary,
            Value = value,
            Source = "human",
            Author = author,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
