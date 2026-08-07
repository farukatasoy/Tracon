using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Cevrimici degerlendirme is isleyicisinin testleri (Faz 49).</summary>
public sealed class OnlineEvalJobHandlerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public void Kind_OnlineEval_dir()
    {
        var handler = BuildHandler(new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant)), new InMemoryRunInputStore(), new InMemoryRunScoreStore(), []);
        handler.Kind.ShouldBe(JobKind.OnlineEval);
    }

    [Fact]
    public async Task Calistirma_bulunamazsa_hata_uretmez_ve_puan_yazmaz()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, new InMemoryRunInputStore(), scores, [new ScriptedJudge("model", static _ => new RunJudgment { Score = 80 })]);

        var runId = Guid.NewGuid();
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_inputs_kaydi_yoksa_orneklenmez_hata_uretmez()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var runId = await SeedRunAsync(runs, withOutput: true);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, new InMemoryRunInputStore(), scores, [new ScriptedJudge("model", static _ => new RunJudgment { Score = 80 })]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Kayitli_yargic_yoksa_hicbir_sey_yazilmaz()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, []);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cikti_okunamiyorsa_puan_yazilmaz()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: false, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [new ScriptedJudge("model", static _ => new RunJudgment { Score = 80 })]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);
        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Puan_dogru_alanlarla_yazilir()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [new ScriptedJudge("model", static _ => new RunJudgment { Score = 42, Reason = "gerekce" })]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        reported.Single().Status.ShouldBe(JobItemStatus.Completed);

        var saved = (await scores.ListAsync(Tenant, runId)).Single();
        saved.Kind.ShouldBe(RunScoreKind.Numeric);
        saved.Value.ShouldBe(42);
        saved.Comment.ShouldBe("gerekce");
        saved.Source.ShouldBe("judge:model");
        saved.Author.ShouldBe("judge:model");
    }

    [Fact]
    public async Task Karar_verilemeyen_yargic_sessiz_sifir_yazmaz()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(
            runs,
            inputs,
            scores,
            [new ScriptedJudge("model", static _ => new RunJudgment { Score = null, Reason = "belirsiz" })]);
        var reported = new List<JobItemResult>();

        await handler.ExecuteAsync(ExecutionContext(runId, reported));

        (await scores.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bir_yargic_hata_verirse_is_geri_adimli_yeniden_denenir_digeri_yine_de_yazar()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var okJudge = new ScriptedJudge("iyi", static _ => new RunJudgment { Score = 70 });
        var badJudge = new ScriptedJudge("kotu", static _ => throw new InvalidOperationException("model coktu"));

        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [okJudge, badJudge]);
        var reported = new List<JobItemResult>();

        var exception = await Should.ThrowAsync<JobRetryException>(
            () => handler.ExecuteAsync(ExecutionContext(runId, reported)).AsTask());

        exception.RetryAfter.ShouldNotBeNull();
        exception.RetryAfter!.Value.ShouldBeGreaterThan(TimeSpan.Zero);

        // Basarili yargicin puani, is yeniden denense de KAYITLIDIR.
        var saved = (await scores.ListAsync(Tenant, runId)).Single();
        saved.Source.ShouldBe("judge:iyi");

        // Is basarisiz oldugu icin oge hic raporlanmadi (Pending kalir).
        reported.ShouldBeEmpty();
    }

    [Fact]
    public async Task JudgeRunAsync_ikinci_kez_cagrilinca_ayni_yargicin_satiri_guncellenir_ikiye_katlanmaz()
    {
        var runs = new InMemoryRunStore(tenantContext: new FixedTenantContext(Tenant));
        var inputs = new InMemoryRunInputStore();
        var runId = await SeedRunAsync(runs, withOutput: true, inputs: inputs);

        var judge = new ScriptedJudge("model", static _ => new RunJudgment { Score = 55 });
        var scores = new InMemoryRunScoreStore();
        var handler = BuildHandler(runs, inputs, scores, [judge]);

        var run = await runs.GetRunAsync(runId);

        await handler.JudgeRunAsync(run!);
        await handler.JudgeRunAsync(run!);

        (await scores.ListAsync(Tenant, runId)).Count.ShouldBe(1);
    }

    private static async Task<Guid> SeedRunAsync(
        InMemoryRunStore runs,
        bool withOutput,
        InMemoryRunInputStore? inputs = null)
    {
        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = Agent,
            TenantId = Tenant,
            StartedAt = now,
        });

        if (inputs is not null)
        {
            await inputs.SaveAsync(new RunInputRecord
            {
                RunId = runId,
                TenantId = Tenant,
                Messages = [new ChatMessage(ChatRole.User, "merhaba")],
                CreatedAt = now,
            });
        }

        if (withOutput)
        {
            await runs.AppendEventAsync(new RunEvent
            {
                RunId = runId,
                Sequence = 0,
                Type = RunEventType.MessageCompleted,
                Timestamp = now,
                Text = "cevap metni",
            });
        }

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
        });

        return runId;
    }

    private static JobContext ExecutionContext(Guid runId, List<JobItemResult> reported) => new()
    {
        Job = Job(),
        Items = [Item(runId)],
        ReportItemAsync = (result, _) =>
        {
            reported.Add(result);
            return default;
        },
        IsCancelledAsync = _ => new ValueTask<bool>(false),
    };

    private static JobRecord Job() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        Kind = JobKind.OnlineEval,
        TargetName = Agent,
        Status = JobStatus.Running,
        Attempt = 1,
        ScheduledFor = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static JobItemRecord Item(Guid runId)
        => new() { Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Seq = 0, Input = runId.ToString(), Status = JobItemStatus.Pending };

    private static OnlineEvalJobHandler BuildHandler(
        InMemoryRunStore runs,
        InMemoryRunInputStore inputs,
        InMemoryRunScoreStore scores,
        IReadOnlyList<IRunJudge> judges)
        => new(runs, inputs, scores, judges, new FixedTenantContext(Tenant));

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class ScriptedJudge(string name, Func<RunJudgeContext, RunJudgment> respond) : IRunJudge
    {
        public string Name => name;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
            => new(respond(context));
    }
}
