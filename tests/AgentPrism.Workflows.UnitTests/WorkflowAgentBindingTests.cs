using AgentPrism.Workflows.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Kodda tanimli workflow'larin agent baglamasi.
/// </summary>
/// <remarks>
/// Bu testin varlik sebebi olculmus bir hatadir: ornek uygulamada agent'lar
/// katalogdan DOGRUDAN alinmisti ve her biri kendi kok <c>runs</c> satirini
/// acti; workflow agaci uc satir yerine bir satir dondu. Sarmalama unutuldugu
/// an ayni sessiz bozulma tekrarlanir.
/// </remarks>
public sealed class WorkflowAgentBindingTests
{
    [Fact]
    public async Task Baglanan_agentler_workflow_agacina_girer()
    {
        var host = new WorkflowTestHost("ozetleyici", "cevirmen");
        var services = BuildServices(host);

        var runner = host.CreateRunner(
            configure: null,
            services,
            new CodeWorkflowRegistration(
                "ozetle-ve-cevir",
                "Kodda tanimli zincir.",
                provider => AgentWorkflowBuilder.BuildSequential(
                    "ozetle-ve-cevir",
                    [
                        provider.GetWorkflowAgent("ozetle-ve-cevir", "ozetleyici"),
                        provider.GetWorkflowAgent("ozetle-ve-cevir", "cevirmen"),
                    ])));

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "ozetle-ve-cevir",
            Message = "girdi",
        }))
        {
            // Olaylar bu testin konusu degil; agac kaydi kontrol edilir.
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);
        var children = runs.Where(run => run.Kind == RunKind.Agent).ToList();

        children.Count.ShouldBe(2);
        children.ShouldAllBe(run => run.ParentRunId == root.Id);
        children.ShouldAllBe(run => run.Depth == 1);

        // Kok listede TEK basina durur: alt calistirmalar bagimsiz kok olarak
        // gorunseydi bu sorgu uc satir dondururdu.
        (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 100 })).Count.ShouldBe(1);
    }

    [Fact]
    public void Baglanan_agent_adi_ve_aciklamasi_korunur()
    {
        var host = new WorkflowTestHost("ozetleyici");
        var services = BuildServices(host);

        var agent = services.GetWorkflowAgent("zincir", "ozetleyici", "Metni ozetler.");

        agent.Name.ShouldBe("ozetleyici");
        agent.Description.ShouldBe("Metni ozetler.");
    }

    [Fact]
    public void Bos_agent_adi_reddedilir()
    {
        var host = new WorkflowTestHost("ozetleyici");
        var services = BuildServices(host);

        Should.Throw<ArgumentException>(() => services.GetWorkflowAgent("zincir", "  "));
    }

    /// <summary>
    /// <see cref="WorkflowAgentBinding.GetWorkflowAgent"/> icin gereken en kucuk kap.
    /// </summary>
    private static ServiceProvider BuildServices(WorkflowTestHost host)
        => new ServiceCollection()
            .AddSingleton(host.Resolver)
            .AddSingleton(host.AgentCache)
            .AddSingleton<ITenantContext>(host.TenantContext)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
}
