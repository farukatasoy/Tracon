namespace AgentPrism.Core.UnitTests.Evaluation;

public sealed class InMemoryEvalStoreTests
{
    [Fact]
    public async Task Takim_kaydedilir_ve_okunur()
    {
        var store = new InMemoryEvalStore();

        var saved = await store.SaveSuiteAsync(Suite("kiraci", "musteri-destek"));

        saved.Id.ShouldNotBe(Guid.Empty);
        saved.CreatedAt.ShouldNotBe(default);

        var loaded = await store.GetSuiteAsync("kiraci", "musteri-destek");
        loaded.ShouldNotBeNull();
        loaded!.Id.ShouldBe(saved.Id);
    }

    [Fact]
    public async Task Baska_kiracinin_takimi_gorunmez()
    {
        var store = new InMemoryEvalStore();
        await store.SaveSuiteAsync(Suite("kiraci-a", "takim"));

        var loaded = await store.GetSuiteAsync("kiraci-b", "takim");

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task Takim_silindiginde_vakalar_ve_kosular_da_silinir()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("kiraci", "takim"));

        await store.ReplaceCasesAsync(suite.Id, [Case("soru-1")]);
        var run = await store.CreateRunAsync(Run("kiraci", suite.Id));

        var deleted = await store.DeleteSuiteAsync("kiraci", "takim");

        deleted.ShouldBeTrue();
        (await store.ListCasesAsync(suite.Id)).ShouldBeEmpty();
        (await store.GetRunAsync("kiraci", run.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task ReplaceCasesAsync_sira_numaralarini_yeniden_atar()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("kiraci", "takim"));

        var saved = await store.ReplaceCasesAsync(suite.Id, [Case("birinci"), Case("ikinci")]);

        saved[0].Seq.ShouldBe(0);
        saved[1].Seq.ShouldBe(1);
        saved[0].SuiteId.ShouldBe(suite.Id);

        // Ikinci cagri oncekileri tamamen degistirir.
        var replaced = await store.ReplaceCasesAsync(suite.Id, [Case("tek")]);
        replaced.Count.ShouldBe(1);
        (await store.ListCasesAsync(suite.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Kosu_yasam_dongusu_calisiyor_tamamlandi_gecisi_yapar()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("kiraci", "takim"));
        var run = await store.CreateRunAsync(Run("kiraci", suite.Id));

        run.Status.ShouldBe(EvalRunStatus.Pending);

        await store.MarkRunRunningAsync(run.Id, agentVersion: 3, modelId: "gpt-x");
        var running = await store.GetRunAsync("kiraci", run.Id);
        running!.Status.ShouldBe(EvalRunStatus.Running);
        running.AgentVersion.ShouldBe(3);
        running.ModelId.ShouldBe("gpt-x");

        await store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = run.Id,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = 2,
            Passed = 1,
            Failed = 1,
        });

        var completed = await store.GetRunAsync("kiraci", run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Completed);
        completed.Passed.ShouldBe(1);
        completed.Failed.ShouldBe(1);
        completed.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetRunByJobIdAsync_dogru_kosuyu_bulur()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("kiraci", "takim"));
        var jobId = Guid.NewGuid();
        var run = await store.CreateRunAsync(Run("kiraci", suite.Id) with { JobId = jobId });

        var found = await store.GetRunByJobIdAsync("kiraci", jobId);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(run.Id);
    }

    [Fact]
    public async Task Vaka_sonucu_kaydedilir_ve_kiraciya_gore_filtrelenir()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("kiraci", "takim"));
        var run = await store.CreateRunAsync(Run("kiraci", suite.Id));

        await store.RecordCaseResultAsync(new EvalCaseResult
        {
            EvalRunId = run.Id,
            CaseId = Guid.NewGuid(),
            Passed = true,
            Output = "cevap",
        });

        var results = await store.ListCaseResultsAsync("kiraci", run.Id);
        results.Count.ShouldBe(1);
        results[0].Output.ShouldBe("cevap");

        (await store.ListCaseResultsAsync("baska-kiraci", run.Id)).ShouldBeEmpty();
    }

    private static EvalSuite Suite(string tenantId, string name) => new()
    {
        TenantId = tenantId,
        Name = name,
        AgentName = "musteri-destek-agent",
    };

    private static EvalCase Case(string query) => new()
    {
        SuiteId = Guid.Empty,
        Seq = 0,
        Query = query,
    };

    private static EvalRun Run(string tenantId, Guid suiteId) => new()
    {
        Id = Guid.Empty,
        TenantId = tenantId,
        SuiteId = suiteId,
        Status = EvalRunStatus.Pending,
        Total = 1,
        StartedAt = DateTimeOffset.UtcNow,
    };
}
