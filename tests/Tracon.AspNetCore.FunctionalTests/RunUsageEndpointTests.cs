using System.Net;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.NodeUsageHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET /api/runs/{runId}/usage</c> lists a run's reported items to its own tenant and to nobody else.
/// </summary>
/// <remarks>
/// The answer for another tenant's run is the answer for a run that does not exist: a 404 with the
/// same body, so the endpoint cannot be used to find out which run ids exist.
/// </remarks>
public sealed class RunUsageEndpointTests
{
    [Fact]
    public async Task The_owning_tenant_reads_the_items_and_another_tenant_gets_the_same_404_as_for_a_missing_run()
    {
        await using var host = await StartAsync(
            builder =>
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });

                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "narrate",
                    _ => async (messages, context, _) =>
                    {
                        await context.ReportUsageAsync(Speech(0.25m));

                        return messages;
                    });
            });

        using (var saved = await PutWorkflowAsync(host, "tenant-a"))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var runId = await RunAsync(host, tenant: "tenant-a");

        // The second direction first: the owner reads its own items.
        (await UsageAsync(host, runId, tenant: "tenant-a")).ShouldHaveSingleItem().Usage.Cost.ShouldBe(0.25m);

        using var intruder = await GetUsageAsync(host, runId, "tenant-b");
        using var missing = await GetUsageAsync(host, Guid.NewGuid(), "tenant-b");

        intruder.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await TraconTestHost.ReadJsonAsync(intruder)).GetProperty("title").GetString()
            .ShouldBe((await TraconTestHost.ReadJsonAsync(missing)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_run_that_reported_nothing_answers_an_empty_list_and_an_unknown_run_answers_404()
    {
        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "tidy",
                _ => (messages, _, _) => Pass(messages)));

        await SaveAsync(host, Workflow, Function("tidy"));

        var runId = await RunAsync(host);

        (await UsageAsync(host, runId)).ShouldBeEmpty();

        using var missing = await GetUsageAsync(host, Guid.NewGuid(), tenant: null);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> GetUsageAsync(TraconTestHost host, Guid runId, string? tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/runs/{runId}/usage");

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        return host.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PutWorkflowAsync(TraconTestHost host, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/tracon/api/workflows/{Workflow}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes = [Function("narrate")],
            }),
        };

        request.Headers.Add("X-Tracon-Tenant", tenant);

        return host.Client.SendAsync(request);
    }
}
