using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// MT-WF-062: bir <c>respond</c> cagrisi, kontrol noktasindan devam etmek
/// yerine grafin GIRIS dugumunu (bir agent-host oldugunda) SIFIRDAN yeniden
/// tetikliyordu.
/// </summary>
public sealed class WorkflowAgentEntryRespondTests
{
    [Fact]
    public async Task Giris_dugumu_agent_ise_respond_onu_yeniden_calistirmaz()
    {
        var host = new WorkflowTestHost("ozetleyici");
        var summarizer = await host.Resolver.ResolveAsync("ozetleyici");

        var runner = host.CreateRunner(
            configure: null,
            services: null,
            AgentApprovalWorkflow.Registration(summarizer!));

        await Collect(runner, "ozetleyici-onay-akisi", "rapor metni");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

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

        // Kok kusur: 'respond' 'ozetleyici'yi SIFIRDAN yeniden calistirirdi -
        // ikinci bir agent-turu 'runs' satiri VE karsiliksiz ikinci bir
        // WorkflowRequest uretirdi, akis yine AwaitingInput ile biterdi
        // (asagidaki iki iddia de bunu yakalar).
        var agentRuns = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.Kind == RunKind.Agent && string.Equals(run.AgentName, "ozetleyici", StringComparison.Ordinal))
            .ToList();

        agentRuns.ShouldHaveSingleItem();

        var second = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow && run.Id != first.Id);

        second.Status.ShouldBe(RunStatus.Completed);

        resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput).Text.ShouldBe("onaylandi");
    }

    private static async Task Collect(WorkflowRunner runner, string name, string message)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = name, Message = message }))
        {
        }
    }
}
