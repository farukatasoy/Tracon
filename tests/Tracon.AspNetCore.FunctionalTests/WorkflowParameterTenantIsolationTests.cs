using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Two tenants with a workflow of the same name never see each other's values.
/// </summary>
/// <remarks>
/// 🚨 F-294: the bound step is compiled per call and skips the compiled-agent
/// cache, whose key once lacked the tenant (a workflow name and an agent name are
/// unique only WITHIN a tenant). The schema comes from the tenant's own
/// definition, the values from the tenant's own request, and the record under the
/// tenant's own id; this proves none of the three crosses over, with the two
/// tenants running at the same moment.
/// </remarks>
public sealed class WorkflowParameterTenantIsolationTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task Two_tenants_running_the_same_workflow_name_keep_their_values_and_records_apart()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await StartAsync(provider);

        // Same workflow name, different default for 'voice' in each tenant.
        await SaveAsync(host, "tenant-a", voiceDefault: "alloy");
        await SaveAsync(host, "tenant-b", voiceDefault: "echo");

        var runs = await Task.WhenAll(
            RunAsync(host, "tenant-a", tone: "formal"),
            RunAsync(host, "tenant-b", tone: "casual"));

        var instructions = provider.Requests.Select(static request => request.Options!.Instructions).ToList();

        instructions.ShouldContain("Edit for a formal tone, voice alloy.", StringComparer.Ordinal);
        instructions.ShouldContain("Edit for a casual tone, voice echo.", StringComparer.Ordinal);
        instructions.ShouldNotContain("Edit for a formal tone, voice echo.", StringComparer.Ordinal);
        instructions.ShouldNotContain("Edit for a casual tone, voice alloy.", StringComparer.Ordinal);

        var inputs = host.Services.GetRequiredService<IRunInputStore>();

        var recordA = await inputs.GetAsync("tenant-a", runs[0]);
        var recordB = await inputs.GetAsync("tenant-b", runs[1]);

        recordA.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "formal");
        recordB.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "casual");

        // A tenant cannot read the other's record, even with the id.
        (await inputs.GetAsync("tenant-b", runs[0])).ShouldBeNull();
        (await inputs.GetAsync("tenant-a", runs[1])).ShouldBeNull();
    }

    [Fact]
    public async Task Another_tenant_cannot_continue_a_run_and_so_cannot_reach_its_values()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        await using var host = await StartAsync(provider);

        await SaveAsync(host, "tenant-a", voiceDefault: "alloy");
        await SaveAsync(host, "tenant-b", voiceDefault: "echo");

        var runId = await RunAsync(host, "tenant-a", tone: "formal");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/runs/{runId}/resume")
        {
            Content = JsonContent.Create(new WorkflowResumeHttpRequest()),
        };

        request.Headers.Add(TenantHeader, "tenant-b");

        using var response = await host.Client.SendAsync(request);
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        // The stream opens (the endpoint cannot tell yet) and ends with an error
        // frame that says the run does not exist - the same text a missing run gets.
        var error = frames.Single(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));

        JsonDocument.Parse(error.Data).RootElement.GetProperty("message").GetString()
            .ShouldNotBeNull().ShouldContain("There is no run", Case.Sensitive);
    }

    /// <summary>The standard parameterized host, with tenancy resolved from the header.</summary>
    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => WorkflowParameterBindingTests.StartAsync(
            provider,
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

    private static async Task SaveAsync(TraconTestHost host, string tenant, string voiceDefault)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/tracon/api/workflows/podcast")
        {
            Content = JsonContent.Create(new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                AgentNames = ["writer", "editor"],
                Parameters =
                [
                    WorkflowParameterBindingTests.Tone(required: true),
                    new AgentParameter { Name = "voice", Kind = AgentParameterKind.Text, DefaultValue = voiceDefault },
                ],
            }),
        };

        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> RunAsync(TraconTestHost host, string tenant, string tone)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/workflows/podcast/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest
            {
                Message = "Episode 12",
                Parameters = WorkflowParameterBindingTests.Values(("tone", tone)),
            }),
        };

        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var accepted = frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal));

        return JsonDocument.Parse(accepted.Data).RootElement.GetProperty("runId").GetGuid();
    }
}
