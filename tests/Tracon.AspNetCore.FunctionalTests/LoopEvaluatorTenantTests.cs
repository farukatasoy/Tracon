using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure mode 3: one factory-built criterion serves every tenant, so
/// whatever it reads at creation belongs to the first tenant that compiled it. A
/// criterion that reads the tenant when it EVALUATES sees each run's own tenant —
/// on a direct agent run and inside a workflow step.
/// </summary>
/// <remarks>
/// The tenant is resolved per request (<c>HttpTenantContext</c>) and has to flow
/// through MAF's loop into the criterion; a unit test with a fixed tenant context
/// proves nothing about that flow.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class LoopEvaluatorTenantTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string ProviderName = "tenant-looping";
    private const string ModelName = "loop-model";
    private const string AgentName = "tenant-looper";

    [Fact]
    public async Task A_criterion_reads_the_tenant_of_the_run_it_evaluates_not_the_tenant_that_created_it()
    {
        var created = new ConcurrentQueue<string>();
        var evaluated = new ConcurrentQueue<string>();
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, static cfg => cfg.EchoesUserMessage());

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                builder.UseWorkflows();
                builder.AddModelProvider(provider);
                builder.AddLoopEvaluator(
                    "tenant-probe",
                    services =>
                    {
                        var tenants = services.GetRequiredService<ITenantContext>();

                        // What a factory reads here is the FIRST compilation's tenant.
                        created.Enqueue(tenants.TenantId);

                        return new DelegateLoopEvaluator((_, _) =>
                        {
                            evaluated.Enqueue(tenants.TenantId);
                            return new ValueTask<LoopEvaluation>(LoopEvaluation.Stop());
                        });
                    });
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Answers once.",
                    Instructions = "Answer.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Harness = new HarnessSettings
                    {
                        Loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = "tenant-probe" }] },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        await RunAgentAsync(host, "tenant-a");
        await RunAgentAsync(host, "tenant-b");

        await SaveWorkflowAsync(host, "tenant-b");
        await RunWorkflowAsync(host, "tenant-b");

        created.ShouldBe(["tenant-a"]);
        evaluated.ShouldBe(["tenant-a", "tenant-b", "tenant-b"]);
    }

    private static async Task RunAgentAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task SaveWorkflowAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/tracon/api/workflows/probe-chain")
        {
            Content = JsonContent.Create(new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                AgentNames = [AgentName],
            }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task RunWorkflowAsync(TraconTestHost host, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/workflows/probe-chain/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "hello" }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
#pragma warning restore MAAI001
