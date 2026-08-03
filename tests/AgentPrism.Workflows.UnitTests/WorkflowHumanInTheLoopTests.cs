using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Insan girdisi bekleyen calistirmanin tam dongusu: bekle, listele, yanitla, bit.
/// </summary>
public sealed class WorkflowHumanInTheLoopTests
{
    [Fact]
    public async Task Bekleyen_istek_calistirmayi_AwaitingInput_yapar()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        var events = await Collect(runner, "onay-akisi", "raporu yayinla");

        // Akisin son olayi hata degil, bekleme bildirmelidir.
        events[^1].Type.ShouldBe(RunEventType.RunAwaitingInput);
        events.ShouldContain(runEvent => runEvent.Type == RunEventType.WorkflowRequest);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput);
        run.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Bekleyen_istek_kontrol_noktasi_yazar()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        // Kontrol noktasi olmadan yanit verilemezdi: yurutme durumu yalnizca
        // orada yasar.
        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, run.Id);

        checkpoints.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Bekleyen_istek_listelenir_ve_sorusunu_tasir()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = await runner.ListPendingRequestsAsync(run.Id);

        var request = pending.ShouldHaveSingleItem();

        request.RunId.ShouldBe(run.Id);
        request.PortId.ShouldBe(ApprovalWorkflow.PortId);
        request.RequestId.ShouldNotBeNullOrWhiteSpace();

        // Yanit tipi bool oldugu icin arayuz evet/hayir sormalidir.
        request.Form.ShouldBe(WorkflowRequestForm.Boolean);
        request.Prompt.ShouldNotBeNull();
        request.Prompt!.ShouldContain("raporu yayinla", Case.Sensitive);
    }

    [Fact]
    public async Task Yanit_verilince_calistirma_tamamlanir()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = await CollectResponse(runner, first.Id, pending.RequestId, approved: true);

        // Sürdürme YENI bir satir acar; eski satir gecmise donuk degistirilmez.
        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 });
        var second = runs.Single(run => run.Id != first.Id);

        second.Status.ShouldBe(RunStatus.Completed);
        second.WorkflowName.ShouldBe("onay-akisi");

        var output = resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput);

        output.Text.ShouldBe("onaylandi");
    }

    [Fact]
    public async Task Ret_yaniti_da_yurutmeye_akar()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = await CollectResponse(runner, first.Id, pending.RequestId, approved: false);

        resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput).Text.ShouldBe("reddedildi");
    }

    [Fact]
    public async Task Yanitlanan_calistirma_artik_bekleyen_istek_gostermez()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        await CollectResponse(runner, first.Id, pending.RequestId, approved: true);

        // Ikinci satir tamamlandi; bekleyen istegi yoktur.
        var second = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 }))
            .Single(run => run.Id != first.Id);

        (await runner.ListPendingRequestsAsync(second.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bilinmeyen_istek_kimligi_reddedilir()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        // Sessizce sürdürulseydi calistirma yine bekleyerek biterdi ve kullanici
        // yanitinin neden ise yaramadigini goremezdi.
        var error = await Should.ThrowAsync<AgentPrismException>(
            async () => await CollectResponse(runner, run.Id, "yok-boyle-bir-istek", approved: true));

        error.Message.ShouldContain("yok-boyle-bir-istek", Case.Sensitive);
    }

    [Fact]
    public async Task Beklemeyen_calistirma_yanitlanamaz()
    {
        var host = new WorkflowTestHost("yazar");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar"],
        });

        var runner = host.CreateRunner();

        await Collect(runner, "zincir", "girdi");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        var error = await Should.ThrowAsync<AgentPrismException>(
            async () => await CollectResponse(runner, run.Id, "herhangi", approved: true));

        error.Message.ShouldContain("beklemiyor", Case.Sensitive);
    }

    [Fact]
    public async Task Baska_kiracinin_calistirmasi_yanitlanamaz()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(run.Id)).Single();

        host.TenantContext.TenantId = "baska-kiraci";

        // "Yetkisiz" bile denmez: baska bir kiracinin calistirmasinin var oldugu
        // bilgisi de sizdirilmaz.
        var error = await Should.ThrowAsync<AgentPrismException>(
            async () => await CollectResponse(runner, run.Id, pending.RequestId, approved: true));

        error.Message.ShouldContain("bulunamadi", Case.Sensitive);
    }

    [Fact]
    public async Task Kontrol_noktasi_kapaliyken_bekleme_hata_verir()
    {
        var host = new WorkflowTestHost();

        var runner = host.CreateRunner(
            options => options.EnableCheckpointing = false,
            services: null,
            ApprovalWorkflow.Registration());

        var events = await Collect(runner, "onay-akisi", "raporu yayinla");

        // Sürdürulemeyecek bir beklemeyi "bekliyor" diye gostermek, kullaniciyi
        // hic gelmeyecek bir devam icin bekletirdi.
        events[^1].Type.ShouldBe(RunEventType.RunFailed);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Message.ShouldContain("EnableCheckpointing", Case.Sensitive);
    }

    [Fact]
    public async Task Bekleyen_calistirmanin_kontrol_noktalari_temizlik_ayarinda_bile_KALIR()
    {
        var host = new WorkflowTestHost();

        var runner = host.CreateRunner(
            options => options.KeepCheckpointsAfterCompletion = false,
            services: null,
            ApprovalWorkflow.Registration());

        await Collect(runner, "onay-akisi", "raporu yayinla");

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        // Silinselerdi yanit verilecek bir yer kalmazdi.
        (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, run.Id)).ShouldNotBeEmpty();
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

    private static async Task<List<RunEvent>> CollectResponse(
        WorkflowRunner runner,
        Guid runId,
        string requestId,
        bool approved)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = runId,
            RequestId = requestId,
            Approved = approved,
        }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
