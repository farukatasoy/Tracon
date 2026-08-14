using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// <c>Magentic</c> deseninin plan onayi.
/// </summary>
/// <remarks>
/// Faz 15'te bu akis <em>kapatilmisti</em>: MAF ilk super-step sonunda bir plan
/// onayi istegi yayinliyor ve yurutme bekleyerek kaliyordu, yanit verecek bir
/// yol da yoktu. Faz 16 human-in-the-loop akisini getirdiginde onay acilabilir
/// hale geldi; asagidaki testler onun gercekten calistigini kanitlar.
/// </remarks>
public sealed class WorkflowPlanApprovalTests
{
    [Fact]
    public void Plan_onayi_yalnizca_Magentic_deseninde_kabul_edilir()
    {
        var message = WorkflowDefinitionValidator.Validate(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["bir", "iki"],
            RequirePlanApproval = true,
        });

        message.ShouldNotBeNull();
        message!.ShouldContain("requirePlanApproval", Case.Sensitive);
    }

    [Fact]
    public void Magentic_plan_onayini_kabul_eder()
        => WorkflowDefinitionValidator.Validate(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["isci"],
            ManagerAgentName = "yonetici",
            RequirePlanApproval = true,
        }).ShouldBeNull();

    [Fact]
    public async Task Plan_onayi_acikken_calistirma_insan_bekler()
    {
        var host = new WorkflowTestHost("yonetici", "isci");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["isci"],
            ManagerAgentName = "yonetici",
            MaxIterations = 2,
            RequirePlanApproval = true,
        });

        var runner = host.CreateRunner();
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "raporu hazirla",
        }))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunAwaitingInput);

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldBe(RunStatus.AwaitingInput);

        // Arayuz plan onayini ayri bir kartla sorar: onayla ya da duzeltme
        // metniyle geri gonder.
        var pending = (await runner.ListPendingRequestsAsync(run.Id)).ShouldHaveSingleItem();

        pending.Form.ShouldBe(WorkflowRequestForm.PlanReview);
        pending.Prompt.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Plan_onayi_kapaliyken_calistirma_beklemez()
    {
        var host = new WorkflowTestHost("yonetici", "isci");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["isci"],
            ManagerAgentName = "yonetici",
            MaxIterations = 2,
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "raporu hazirla",
        }))
        {
            // Olaylar bu testin konusu degil.
        }

        var run = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();

        run.Status.ShouldNotBe(RunStatus.AwaitingInput);
    }

    [Fact]
    public async Task Plan_onaylaninca_yurutme_devam_eder()
    {
        var host = new WorkflowTestHost("yonetici", "isci");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["isci"],
            ManagerAgentName = "yonetici",
            MaxIterations = 2,
            RequirePlanApproval = true,
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "raporu hazirla",
        }))
        {
            // Ilk tur yalnizca plani kurar.
        }

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = first.Id,
            RequestId = pending.RequestId,
            Approved = true,
        }))
        {
            resumed.Add(runEvent);
        }

        // Onaydan sonra graf gercekten ilerler: yonetici yeniden calisir.
        resumed.ShouldContain(runEvent => runEvent.Type == RunEventType.ExecutorInvoked);

        // Sürdürme YENI bir satir acar; eski satir gecmise donuk degistirilmez.
        var second = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 }))
            .Single(run => run.Id != first.Id);

        second.WorkflowName.ShouldBe("magentic");

        // Ikinci turun yeniden onay istemesi BEKLENEN davranistir: sahte
        // yonetici anlamli bir plan uretmez, MAF de yeniden planlar ve plani
        // tekrar onaya sunar. Onemli olan dongunun tikanmamasi - her turda
        // yanit verilebilir bir istek uretilir.
        if (second.Status == RunStatus.AwaitingInput)
        {
            (await runner.ListPendingRequestsAsync(second.Id)).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task Plan_reddedilirken_duzeltme_metni_zorunludur()
    {
        var host = new WorkflowTestHost("yonetici", "isci");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["isci"],
            ManagerAgentName = "yonetici",
            MaxIterations = 2,
            RequirePlanApproval = true,
        });

        var runner = host.CreateRunner();

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "magentic",
            Message = "raporu hazirla",
        }))
        {
            // Ilk tur yalnizca plani kurar.
        }

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 10 })).Single();
        var pending = (await runner.ListPendingRequestsAsync(first.Id)).Single();

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = first.Id,
            RequestId = pending.RequestId,
            Approved = false,
        }))
        {
            resumed.Add(runEvent);
        }

        // Duzeltme metni olmadan yonetici agent plani neye gore yeniden
        // kuracagini bilemez; sessizce onaylamak yaniltici olurdu.
        var failure = resumed[^1];

        failure.Type.ShouldBe(RunEventType.RunFailed);
        failure.Text.ShouldNotBeNull();
        failure.Text!.ShouldContain("revision text", Case.Sensitive);
    }
}
