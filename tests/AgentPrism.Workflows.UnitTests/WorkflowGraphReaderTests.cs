using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Grafin derlenmis workflow'dan cikarilmasi.
/// </summary>
/// <remarks>
/// En kritik sozlesme <strong>dugum kimlikleridir</strong>: arayuz dugumleri
/// calistirma olaylarindaki <c>ExecutorInvoked</c> / <c>ExecutorCompleted</c>
/// metinleriyle eslestirip renklendirir. Kimlikler kayarsa graf cizilir ama
/// hicbir zaman renklenmez - sessiz bir bozulma.
/// </remarks>
public sealed class WorkflowGraphReaderTests
{
    [Fact]
    public async Task Sequential_grafi_agent_dugumlerini_ve_ciktiyi_tasir()
    {
        var host = new WorkflowTestHost("ozetleyici", "cevirmen");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["ozetleyici", "cevirmen"],
        });

        var graph = (await host.CreateRunner().GetGraphAsync("zincir"))!;

        graph.Name.ShouldBe("zincir");
        graph.Mermaid.ShouldNotBeNullOrWhiteSpace();

        var agents = graph.Nodes.Where(node => node.Kind == WorkflowNodeKind.Agent).ToList();

        agents.Count.ShouldBe(2);
        agents.Select(node => node.AgentName).Order(StringComparer.Ordinal)
            .ShouldBe(["cevirmen", "ozetleyici"]);

        // Hazir desen bir cikti dugumu ekler; kullanicinin tanimda yazmadigi
        // bu dugum grafta gorunmezse calistirma olaylari eslesmezdi.
        graph.Nodes.ShouldContain(node => node.Kind == WorkflowNodeKind.Output);

        graph.StartExecutorId.ShouldNotBeNullOrWhiteSpace();
        graph.Nodes.ShouldContain(node => string.Equals(node.Id, graph.StartExecutorId, StringComparison.Ordinal));

        // Her kenarin iki ucu da graftaki bir dugume isaret etmelidir.
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        graph.Edges.ShouldNotBeEmpty();
        graph.Edges.ShouldAllBe(edge => ids.Contains(edge.From) && ids.Contains(edge.To));
    }

    [Fact]
    public async Task Dugum_kimlikleri_calistirma_olaylariyla_ESLESIR()
    {
        var host = new WorkflowTestHost("ozetleyici", "cevirmen");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["ozetleyici", "cevirmen"],
        });

        var runner = host.CreateRunner();
        var graph = (await runner.GetGraphAsync("zincir"))!;
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        var invoked = new List<string>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "zincir",
            Message = "girdi",
        }))
        {
            if (runEvent.Type == RunEventType.ExecutorInvoked && runEvent.Text is { Length: > 0 } id)
            {
                invoked.Add(id);
            }
        }

        invoked.ShouldNotBeEmpty();
        invoked.ShouldAllBe(id => ids.Contains(id));
    }

    [Fact]
    public async Task Dis_istek_portu_ayri_bir_dugum_turudur()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        var graph = (await runner.GetGraphAsync("onay-akisi"))!;

        var port = graph.Nodes.Single(node => node.Kind == WorkflowNodeKind.RequestPort);

        port.Id.ShouldBe(ApprovalWorkflow.PortId);
    }

    [Fact]
    public async Task Concurrent_grafi_dagitici_ve_birlestirici_dugumleri_gosterir()
    {
        var host = new WorkflowTestHost("bir", "iki");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "esz",
            Kind = WorkflowKind.Concurrent,
            AgentNames = ["bir", "iki"],
        });

        var graph = (await host.CreateRunner().GetGraphAsync("esz"))!;

        graph.Nodes.Count(node => node.Kind == WorkflowNodeKind.Agent).ShouldBe(2);
        graph.Nodes.ShouldContain(node => node.Kind == WorkflowNodeKind.Orchestration);

        // Dagitim ve birlestirme kenar turlerinden okunur; arayuz oklari buna
        // gore cizer.
        graph.Edges.ShouldContain(edge => edge.Kind == WorkflowEdgeKind.FanOut);
        graph.Edges.ShouldContain(edge => edge.Kind == WorkflowEdgeKind.FanIn);
    }

    [Fact]
    public async Task Olmayan_workflow_icin_graf_yoktur()
        => (await new WorkflowTestHost().CreateRunner().GetGraphAsync("yok")).ShouldBeNull();
}
