
namespace AgentPrism.StoreContracts;

/// <summary><see cref="IEvalStore"/> sozlesmesinin davranis testleri.</summary>
public abstract class EvalStoreContract : TenantIsolationContract<IEvalStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId, name));
        await Store.CreateRunAsync(TestData.EvalRun(tenantId, suite.Id));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetSuiteAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListSuitesAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteSuiteAsync(tenantId, (string)key);

    [Fact]
    public async Task SaveSuiteAsync_yeni_takima_kimlik_atar()
    {
        var saved = await Store.SaveSuiteAsync(TestData.EvalSuite());

        saved.Id.ShouldNotBe(Guid.Empty);
        saved.CreatedAt.ShouldNotBe(default);
        saved.UpdatedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task SaveSuiteAsync_ayni_ad_icin_kimligi_korur_ve_gunceller()
    {
        var first = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var second = await Store.SaveSuiteAsync(TestData.EvalSuite() with { Description = "guncellendi" });

        second.Id.ShouldBe(first.Id);

        var fetched = await Store.GetSuiteAsync("default", "musteri-destek-takimi");
        fetched!.Description.ShouldBe("guncellendi");
    }

    [Fact]
    public async Task GetSuiteAsync_baska_kiracidan_null_doner()
    {
        await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId: "kiraci-a"));

        (await Store.GetSuiteAsync("kiraci-b", "musteri-destek-takimi")).ShouldBeNull();
    }

    [Fact]
    public async Task ListSuitesAsync_yalnizca_o_kiraciyi_getirir()
    {
        await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId: "kiraci-a", name: "s1"));
        await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId: "kiraci-b", name: "s2"));

        var list = await Store.ListSuitesAsync("kiraci-a");

        list.ShouldHaveSingleItem();
        list[0].Name.ShouldBe("s1");
    }

    [Fact]
    public async Task DeleteSuiteAsync_vakalari_ve_kosulari_da_siler()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id)]);
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id));

        (await Store.DeleteSuiteAsync("default", "musteri-destek-takimi")).ShouldBeTrue();

        (await Store.ListCasesAsync(suite.Id)).ShouldBeEmpty();
        (await Store.GetRunAsync("default", run.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task ReplaceCasesAsync_sira_numaralarini_atar_ve_oncekileri_degistirir()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var saved = await Store.ReplaceCasesAsync(
            suite.Id,
            [TestData.EvalCase(suite.Id, "birinci"), TestData.EvalCase(suite.Id, "ikinci")]);

        saved[0].Seq.ShouldBe(0);
        saved[1].Seq.ShouldBe(1);
        saved[0].SuiteId.ShouldBe(suite.Id);

        var replaced = await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id, "tek")]);

        replaced.Count.ShouldBe(1);
        (await Store.ListCasesAsync(suite.Id)).Count.ShouldBe(1);
        (await Store.ListCasesAsync(suite.Id))[0].Query.ShouldBe("tek");
    }

    [Fact]
    public async Task ReplaceCasesAsync_beklenen_toollari_ve_baglami_saklar()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var input = TestData.EvalCase(suite.Id) with
        {
            ExpectedOutput = "beklenen",
            ExpectedTools = ["get_order_status", "get_shipping_status"],
            Context = "ek baglam",
        };

        await Store.ReplaceCasesAsync(suite.Id, [input]);
        var loaded = (await Store.ListCasesAsync(suite.Id))[0];

        loaded.ExpectedOutput.ShouldBe("beklenen");
        loaded.ExpectedTools.ShouldBe(["get_order_status", "get_shipping_status"]);
        loaded.Context.ShouldBe("ek baglam");
    }

    [Fact]
    public async Task Kosu_yasam_dongusu_calisiyor_tamamlandi_gecisi_yapar()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var jobId = Guid.NewGuid();
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id) with { JobId = jobId });

        run.Status.ShouldBe(EvalRunStatus.Pending);

        await Store.MarkRunRunningAsync(run.Id, agentVersion: 4, modelId: "gpt-test");
        var running = await Store.GetRunAsync("default", run.Id);
        running!.Status.ShouldBe(EvalRunStatus.Running);
        running.AgentVersion.ShouldBe(4);
        running.ModelId.ShouldBe("gpt-test");

        await Store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = run.Id,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = 3,
            Passed = 2,
            Failed = 1,
            InputTokens = 100,
            OutputTokens = 50,
        });

        var completed = await Store.GetRunAsync("default", run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Completed);
        completed.Passed.ShouldBe(2);
        completed.Failed.ShouldBe(1);
        completed.InputTokens.ShouldBe(100);
        completed.OutputTokens.ShouldBe(50);
        completed.CompletedAt.ShouldNotBeNull();

        var byJob = await Store.GetRunByJobIdAsync("default", jobId);
        byJob!.Id.ShouldBe(run.Id);
    }

    [Fact]
    public async Task QueryRunsAsync_takima_gore_filtreler_ve_en_yeniyi_basa_alir()
    {
        var suiteA = await Store.SaveSuiteAsync(TestData.EvalSuite(name: "takim-a"));
        var suiteB = await Store.SaveSuiteAsync(TestData.EvalSuite(name: "takim-b"));

        var first = await Store.CreateRunAsync(TestData.EvalRun("default", suiteA.Id) with { StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
        var second = await Store.CreateRunAsync(TestData.EvalRun("default", suiteA.Id) with { StartedAt = DateTimeOffset.UtcNow });
        await Store.CreateRunAsync(TestData.EvalRun("default", suiteB.Id));

        var runs = await Store.QueryRunsAsync(new EvalRunQuery { TenantId = "default", SuiteId = suiteA.Id });

        runs.Count.ShouldBe(2);
        runs[0].Id.ShouldBe(second.Id);
        runs[1].Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Vaka_sonucu_kaydedilir_ve_kiraciya_gore_filtrelenir()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id));
        var runId = Guid.NewGuid();

        await Store.RecordCaseResultAsync(new EvalCaseResult
        {
            EvalRunId = run.Id,
            CaseId = Guid.NewGuid(),
            RunId = runId,
            Passed = false,
            Output = "cikti",
            Scores = TestData.State("""[{"name":"nonEmpty","passed":false}]"""),
            FailureReason = "cok kisa",
        });

        var results = await Store.ListCaseResultsAsync("default", run.Id);

        results.ShouldHaveSingleItem();
        results[0].Passed.ShouldBeFalse();
        results[0].Output.ShouldBe("cikti");
        results[0].RunId.ShouldBe(runId);
        results[0].FailureReason.ShouldBe("cok kisa");

        (await Store.ListCaseResultsAsync("baska-kiraci", run.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Kosu_okumalari_kiracilar_arasinda_sizmaz()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite("tenant-a", "takim"));
        var jobId = AgentPrismId.NewId();
        var run = await Store.CreateRunAsync(TestData.EvalRun("tenant-a", suite.Id) with { JobId = jobId });

        await Store.RecordCaseResultAsync(new EvalCaseResult
        {
            Id = AgentPrismId.NewId(),
            EvalRunId = run.Id,
            CaseId = AgentPrismId.NewId(),
            Passed = true,
        });

        (await Store.GetRunAsync("tenant-b", run.Id)).ShouldBeNull();
        (await Store.GetRunAsync("tenant-a", run.Id)).ShouldNotBeNull();

        (await Store.GetRunByJobIdAsync("tenant-b", jobId)).ShouldBeNull();
        (await Store.GetRunByJobIdAsync("tenant-a", jobId)).ShouldNotBeNull();

        (await Store.ListCaseResultsAsync("tenant-b", run.Id)).ShouldBeEmpty();
        (await Store.ListCaseResultsAsync("tenant-a", run.Id)).ShouldHaveSingleItem();

        (await Store.QueryRunsAsync(new EvalRunQuery { TenantId = "tenant-b" })).ShouldBeEmpty();
        (await Store.QueryRunsAsync(new EvalRunQuery { TenantId = "tenant-a" })).ShouldHaveSingleItem();
    }
}
