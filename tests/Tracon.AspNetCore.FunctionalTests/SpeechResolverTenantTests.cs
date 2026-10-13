using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued workflow resolves its speech provider under the tenant that queued it,
/// not under the worker's.
/// </summary>
/// <remarks>
/// The resolver takes no tenant parameter: it reads the run scope first and the
/// request's tenant only outside a run. A queued workflow runs on a background
/// worker, where no request exists; two tenants queue the same workflow, one of them
/// with a policy that forbids the speech provider, and only the other one's text may
/// reach it.
/// </remarks>
public sealed class SpeechResolverTenantTests
{
    private const string Provider = "studio-engine";

    [Fact]
    public async Task A_queued_workflow_is_judged_by_the_tenant_that_queued_it()
    {
        var engine = new CountingSpeechProvider(Provider);
        var node = new NamedSpeechProviderTests.NarrateNode();

        await using var host = await WorkflowSettingsFixtures.StartAsync(
            builder =>
            {
                builder.AddSpeechSynthesizer("studio", _ => engine);
                node.Register(builder);
                builder.Services.Configure<TraconSchedulingOptions>(
                    static options => options.PollInterval = TimeSpan.FromMilliseconds(50));
            },
            tenancy: true);

        foreach (var tenant in new[] { "acme", "globex" })
        {
            await SaveOkAsync(host, "narrated", Chain("""{"provider":"studio"}""", NamedSpeechProviderTests.NarrateNode.Name), tenant);
        }

        // acme may reach its model provider only; globex has no policy.
        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("acme", ["echo"], TestContext.Current.CancellationToken);

        var dispatcher = host.Services.GetRequiredService<IJobDispatcher>();

        var acme = await dispatcher.EnqueueAsync(new JobRequest
        {
            TenantId = "acme",
            HandlerKey = JobHandlerKeys.Workflow,
            TargetName = "narrated",
            Items = ["from-acme"],
        });

        var globex = await dispatcher.EnqueueAsync(new JobRequest
        {
            TenantId = "globex",
            HandlerKey = JobHandlerKeys.Workflow,
            TargetName = "narrated",
            Items = ["from-globex"],
        });

        var acmeRun = await ItemRunAsync(host, "acme", acme.Id);
        var globexRun = await ItemRunAsync(host, "globex", globex.Id);

        acmeRun.Status.ShouldBe(RunStatus.Failed);
        acmeRun.Error.ShouldNotBeNull().Message.ShouldContain($"speech provider '{Provider}'");
        globexRun.Status.ShouldBe(RunStatus.Completed, globexRun.Error?.Message);

        engine.Calls.ShouldBe(1);
        engine.Received.ShouldHaveSingleItem().Text.ShouldContain("from-globex");
    }

    private static async Task<RunRecord> ItemRunAsync(TraconTestHost host, string tenant, Guid jobId)
    {
        var jobs = host.Services.GetRequiredService<IJobStore>();

        await WaitUntil.ValueAsync(
            async () => (await jobs.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            $"the {tenant} job to finish");

        var item = (await jobs.ListItemsAsync(jobId)).ShouldHaveSingleItem();
        var runId = item.RunId.ShouldNotBeNull();

        // The run belongs to the tenant that queued it; the store reads the ambient tenant.
        using (AmbientTenantScope.Begin(tenant))
        {
            return (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
        }
    }
}
