using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A rollback is a save of stored content (phase 200, failure mode 17): it is judged the way a
/// save is, it writes nothing when it is refused, and a refusal never reaches the audit trail.
/// </summary>
/// <remarks>
/// The old content was valid when it was saved. A function it names can have been unregistered
/// since and an agent version it pins can have vanished, so writing it back unchecked would make
/// the CURRENT definition one no run can compile - at the moment an operator reaches for the
/// rollback because something is already wrong.
/// </remarks>
public sealed class WorkflowRollbackTests
{
    private const string Name = "narrated";

    [Fact]
    public async Task A_version_that_names_a_function_that_is_no_longer_registered_is_refused_and_nothing_is_written()
    {
        await using var host = await StartAsync();

        // Version 1 reaches the store by the side door a restored backup or a consumer's own code uses.
        var store = host.Services.GetRequiredService<IWorkflowDefinitionStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await store.SaveAsync(tenant, new WorkflowDefinition
        {
            Name = Name,
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = Writer, Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "unregistered-since", Kind = WorkflowNodeKind.Function },
            ],
        });

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        using var response = await RollbackAsync(host, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ProblemOf(response);

        problem.GetProperty("title").GetString().ShouldBe("Workflow definition invalid");

        var detail = problem.GetProperty("detail").GetString()!;

        detail.ShouldStartWith("Version 1 of workflow 'narrated' cannot be restored as it is.");
        detail.ShouldContain("uses function 'unregistered-since', but no such function is registered");

        await AssertStillAtVersionAsync(host, 2);
        await AssertNoRollbackAuditedAsync(host);
    }

    [Fact]
    public async Task A_version_whose_function_settings_no_longer_fit_the_function_is_refused()
    {
        await using var host = await StartAsync();

        var store = host.Services.GetRequiredService<IWorkflowDefinitionStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        // The function's settings type changed since: a member this version writes is gone.
        await store.SaveAsync(tenant, new WorkflowDefinition
        {
            Name = Name,
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = Writer, Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = Tts, Kind = WorkflowNodeKind.Function, Settings = Json("""{"retiredMember":true}""") },
            ],
        });

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        using var response = await RollbackAsync(host, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("detail").GetString()!.ShouldContain("retiredMember");

        await AssertStillAtVersionAsync(host, 2);
        await AssertNoRollbackAuditedAsync(host);
    }

    [Fact]
    public async Task A_version_that_pins_an_agent_version_the_agent_no_longer_has_is_refused()
    {
        var provider = new FakeModelProvider("rollback-provider").WithModel(new ModelDescriptor { Name = "m" }).EchoesUserMessage();

        await using var host = await TraconTestHost.StartAsync(builder => builder.AddModelProvider(provider).UseWorkflows());

        await CreateAgentAsync(host, "alpha", "one");
        await UpdateAgentAsync(host, "alpha", "two");
        await UpdateAgentAsync(host, "alpha", "three");

        using (var pinned = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/workflows/{Name}",
                   new { kind = "Sequential", nodes = new object[] { new { name = "alpha", kind = "Agent", agentVersion = 3 } } }))
        {
            pinned.StatusCode.ShouldBe(HttpStatusCode.OK, await pinned.Content.ReadAsStringAsync());
        }

        using (var plain = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/workflows/{Name}",
                   new { kind = "Sequential", nodes = new object[] { new { name = "alpha", kind = "Agent" } } }))
        {
            plain.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The agent is deleted and created again: its numbering restarted, so version 3 names nothing.
        using (var deleted = await host.Client.DeleteAsync("/tracon/api/agents/alpha"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateAgentAsync(host, "alpha", "fresh");

        using var response = await RollbackAsync(host, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemOf(response)).GetProperty("detail").GetString()!
            .ShouldContain("pins agent 'alpha' to version 3, but the agent has no version 3");

        await AssertStillAtVersionAsync(host, 2);
        await AssertNoRollbackAuditedAsync(host);
    }

    [Fact]
    public async Task A_version_the_workflow_does_not_have_is_not_found_and_nothing_is_written_or_audited()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        foreach (var missing in new[] { 0, -1, 2, 99 })
        {
            using var response = await RollbackAsync(host, missing);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemOf(response)).GetProperty("detail").GetString().ShouldBe($"Workflow '{Name}' has no version {missing}.");
        }

        await AssertStillAtVersionAsync(host, 1);
        await AssertNoRollbackAuditedAsync(host);
    }

    [Fact]
    public async Task A_body_that_is_not_a_rollback_request_is_a_bad_request()
    {
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));

        using var response = await host.Client.PostAsync(
            $"/tracon/api/workflows/{Name}/rollback",
            new StringContent("""{"version":"one"}""", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertStillAtVersionAsync(host, 1);
    }

    [Fact]
    public async Task A_valid_rollback_is_accepted_even_when_the_current_content_is_the_one_that_broke()
    {
        // The reason a rollback exists: the current version is bad, the old one is fine.
        await using var host = await StartAsync();

        await SaveOkAsync(host, Name, Chain("""{"voice":"alloy"}"""));
        await SaveOkAsync(host, Name, Chain("""{"voice":"nova"}"""));

        using var response = await RollbackAsync(host, 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var run = await RunAsync(host, Name);

        run.Output.ShouldBe("voice=alloy;lufs=-16;speed=Normal");
        run.Record.AgentVersion.ShouldBe(3);
    }

    // --- Helpers ---------------------------------------------------------

    private static Task<HttpResponseMessage> RollbackAsync(TraconTestHost host, int version)
        => host.Client.PostAsJsonAsync($"/tracon/api/workflows/{Name}/rollback", new WorkflowRollbackRequest { Version = version });

    private static async Task AssertStillAtVersionAsync(TraconTestHost host, int version)
    {
        using var read = await host.Client.GetAsync($"/tracon/api/workflows/{Name}");

        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("version").GetInt32().ShouldBe(version);

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions");

        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(version);
    }

    private static async Task AssertNoRollbackAuditedAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery
        {
            TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
            Action = "workflow.rollback",
        })).ShouldBeEmpty("a refused rollback is not an action that happened");

    private static async Task CreateAgentAsync(TraconTestHost host, string name, string instructions)
    {
        using var created = await host.Client.PostAsJsonAsync("/tracon/api/agents", AgentRequest(name, instructions));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static async Task UpdateAgentAsync(TraconTestHost host, string name, string instructions)
    {
        using var updated = await host.Client.PutAsJsonAsync($"/tracon/api/agents/{name}", AgentRequest(name, instructions));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
    }

    private static AgentDefinitionRequest AgentRequest(string name, string instructions)
        => new()
        {
            Name = name,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "rollback-provider", Model = "m" },
        };
}
