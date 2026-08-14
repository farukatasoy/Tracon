using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Kosucunun ucuncu tarafa gorunen davranisi: calistirma kaydi, agac,
/// kontrol noktalari ve sinirlar.
/// </summary>
public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task Sequential_calistirmasi_agac_uretir()
    {
        var host = new WorkflowTestHost("yazar", "editor", "kontrol");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor", "kontrol"],
        });

        var runner = host.CreateRunner();
        var events = await Collect(runner, "zincir", "merhaba");

        events.ShouldNotBeEmpty();

        // Kok satir: bir workflow calistirmasi.
        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.WorkflowName.ShouldBe("zincir");
        root.AgentName.ShouldBe("zincir");
        root.Status.ShouldBe(RunStatus.Completed);
        root.Depth.ShouldBe(0);

        // Uc agent satiri, hepsi kokun altinda. Waterfall bu sayede dogru cizilir.
        var children = runs.Where(run => run.Kind == RunKind.Agent).ToList();

        children.Count.ShouldBe(3);
        children.ShouldAllBe(run => run.ParentRunId == root.Id);
        children.ShouldAllBe(run => run.RootRunId == root.Id);
        children.ShouldAllBe(run => run.Depth == 1);
        children.Select(run => run.AgentName).Order(StringComparer.Ordinal)
            .ShouldBe(["editor", "kontrol", "yazar"]);
    }

    [Fact]
    public async Task Zincirin_ciktisi_sonraki_agente_akar()
    {
        var host = new WorkflowTestHost("bir", "iki");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["bir", "iki"],
        });

        var runner = host.CreateRunner();
        var events = await Collect(runner, "zincir", "girdi");

        // EchoAgent gelen metni isaretler; ikinci halka birincinin ciktisini
        // gormezse metin ic ice gecmezdi.
        var output = events.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput);

        output.Text.ShouldNotBeNull();
        output.Text!.ShouldContain("[iki][bir]girdi", Case.Sensitive);
    }

    [Fact]
    public async Task Kontrol_noktalari_yazilir()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "zincir", "merhaba");

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, root.Id);

        checkpoints.ShouldNotBeEmpty();
        checkpoints.ShouldAllBe(record => record.RunId == root.Id);

        // Liste ustveridir; durum yuku bilerek okunmaz.
        checkpoints.ShouldAllBe(record => WorkflowCheckpointState.IsOmitted(record.State));
    }

    [Fact]
    public async Task Checkpoint_kapaliyken_nokta_yazilmaz()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        });

        var runner = host.CreateRunner(options => options.EnableCheckpointing = false);
        await Collect(runner, "zincir", "merhaba");

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, root.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Kontrol_noktasindan_surdurulur()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "zincir", "merhaba");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = first.Id }))
        {
            resumed.Add(runEvent);
        }

        resumed.ShouldNotBeEmpty();

        // Sürdürme YENI bir calistirma kaydi acar: ayni satiri yeniden acmak
        // olay akisinin append-only olma kuralini bozardi.
        var workflowRuns = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.Kind == RunKind.Workflow)
            .ToList();

        workflowRuns.Count.ShouldBe(2);
        workflowRuns.ShouldAllBe(run => run.WorkflowName == "zincir");
        workflowRuns.Select(run => run.SessionId).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
    }

    [Fact]
    public async Task Baska_kiracinin_calistirmasi_surdurulemez()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        });

        var runner = host.CreateRunner();
        await Collect(runner, "zincir", "merhaba");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(record => record.Kind == RunKind.Workflow);

        host.TenantContext.TenantId = "baska-kiraci";

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in runner.ResumeStreamingAsync(new WorkflowResumeRequest { RunId = run.Id }))
            {
                // Akis hic baslamamalidir.
            }
        });

        // Mesaj "yetkisiz" demez: baska bir kiracinin calistirmasinin VAR OLDUGU
        // bilgisi bile sizdirilmaz.
        exception.Message.ShouldContain("There is no run", Case.Sensitive);
        exception.Message.ShouldNotContain("permission", Case.Sensitive);
    }

    [Fact]
    public async Task Bilinmeyen_workflow_calistirmayi_basarisiz_kapatir()
    {
        var host = new WorkflowTestHost("yazar");
        var runner = host.CreateRunner();

        var events = await Collect(runner, "olmayan", "merhaba");

        events[^1].Type.ShouldBe(RunEventType.RunFailed);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(record => record.Kind == RunKind.Workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("There is no workflow named 'olmayan'", Case.Sensitive);
    }

    [Fact]
    public async Task Super_step_siniri_calistirmayi_durdurur()
    {
        var host = new WorkflowTestHost("yazar", "editor", "kontrol");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor", "kontrol"],
        });

        // Sequential zincir dort super-step ister (uc agent + cikti toplayicisi);
        // sinir bir olunca ikinci adimda kesilmelidir.
        var runner = host.CreateRunner(options => options.MaxSuperSteps = 1);
        var events = await Collect(runner, "zincir", "merhaba");

        events[^1].Type.ShouldBe(RunEventType.RunFailed);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(record => record.Kind == RunKind.Workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("super-step sinirini asti", Case.Sensitive);
    }

    [Fact]
    public async Task Motor_kapaliyken_calistirma_reddedilir()
    {
        var host = new WorkflowTestHost("yazar");
        var runner = host.CreateRunner(options => options.Enabled = false);

        await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = "zincir" }))
            {
                // Akis hic baslamamalidir.
            }
        });
    }

    [Fact]
    public async Task Gecersiz_oturum_kimligi_reddedilir()
    {
        var host = new WorkflowTestHost("yazar");
        var runner = host.CreateRunner();

        // Oturum kimligi kontrol noktalarini gruplar ve istemciden gelir.
        // Dogrulanmadan kullanilmasi baska bir yurutmenin durumuna erisim demektir.
        await Should.ThrowAsync<AgentPrismException>(async () =>
        {
            await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
            {
                WorkflowName = "zincir",
                SessionId = "../../etc/passwd",
            }))
            {
                // Akis hic baslamamalidir.
            }
        });
    }

    [Fact]
    public async Task Kodda_tanimli_workflow_veritabanindakinin_onune_gecer()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        });

        var runner = host.CreateRunner(
            configure: null,
            services: null,
            new CodeWorkflowRegistration(
                "zincir",
                "Kodda tanimli.",
                _ => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential("zincir", [])));

        var descriptor = (await runner.GetAsync("zincir"))!;

        descriptor.Origin.ShouldBe(AgentDefinitionOrigin.Code);
        descriptor.Description.ShouldBe("Kodda tanimli.");
    }

    private static async Task<List<RunEvent>> Collect(WorkflowRunner runner, string name, string message)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = name,
            Message = message,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
