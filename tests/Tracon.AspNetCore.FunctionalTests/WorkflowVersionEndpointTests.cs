using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The version history of a stored workflow over HTTP (phase 200): the list, the diff, the
/// rollback, the tenant boundary, the roles and the audit trail.
/// </summary>
public sealed class WorkflowVersionEndpointTests
{
    private const string Name = "narrated";

    [Fact]
    public async Task Two_saves_list_two_snapshots_newest_first_and_the_diff_returns_both_sides_verbatim()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, Name, "first.", ["writer"]);
        await SaveAsync(host, Name, "second.", ["writer", "editor"]);

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var versions = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ToList();

        versions.Select(static version => version.GetProperty("version").GetInt32()).ShouldBe([2, 1]);
        versions.Select(static version => version.GetProperty("description").GetString()).ShouldBe(["second.", "first."]);

        // A snapshot is the whole definition, not a delta.
        versions[1].GetProperty("agentNames").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["writer"]);
        versions[0].GetProperty("agentNames").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["writer", "editor"]);

        using var diff = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions/1/diff/2");

        diff.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(diff);

        json.GetProperty("left").GetProperty("description").GetString().ShouldBe("first.");
        json.GetProperty("right").GetProperty("description").GetString().ShouldBe("second.");

        // The two numbers may be given in either order; the sides follow the request.
        using var reversed = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions/2/diff/1");

        (await TraconTestHost.ReadJsonAsync(reversed)).GetProperty("left").GetProperty("description").GetString().ShouldBe("second.");
    }

    [Fact]
    public async Task A_diff_with_a_missing_version_names_the_one_that_was_not_found()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, Name, "first.", ["writer"]);

        using var diff = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions/1/diff/5");

        diff.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TraconTestHost.ReadJsonAsync(diff)).GetProperty("detail").GetString().ShouldBe("Workflow 'narrated' has no version 5.");
    }

    [Fact]
    public async Task A_rollback_writes_the_old_content_as_a_new_version_and_changes_what_the_next_run_uses()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, Name, "first.", ["writer"]);
        await SaveAsync(host, Name, "second.", ["writer", "editor"]);

        using var rolledBack = await PostRollbackAsync(host, Name, 1);

        rolledBack.StatusCode.ShouldBe(HttpStatusCode.OK, await rolledBack.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(rolledBack);

        body.GetProperty("version").GetInt32().ShouldBe(3);
        body.GetProperty("description").GetString().ShouldBe("first.");

        using var current = await host.Client.GetAsync($"/tracon/api/workflows/{Name}");
        var definition = await TraconTestHost.ReadJsonAsync(current);

        definition.GetProperty("version").GetInt32().ShouldBe(3);
        definition.GetProperty("agentNames").GetArrayLength().ShouldBe(1);

        // History is never rewritten: three versions, and the one that was current stays readable.
        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions");

        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(3);

        // The next run is the old chain, stamped with the rollback's version.
        var run = await RunAsync(host, Name);

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Record.AgentVersion.ShouldBe(3);
        run.ChildAgents.ShouldBe(["writer"]);
    }

    [Fact]
    public async Task Versions_of_a_workflow_defined_in_code_say_so_and_a_rollback_of_it_is_a_conflict()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .UseWorkflows()
            .AddWorkflow(
                "coded",
                static _ => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential("coded", []),
                "Defined in code."));

        using var listed = await host.Client.GetAsync("/tracon/api/workflows/coded/versions");

        listed.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await TraconTestHost.ReadJsonAsync(listed);

        problem.GetProperty("title").GetString().ShouldBe("Workflow has no version history");
        problem.GetProperty("detail").GetString()!.ShouldContain("'coded' is a workflow defined in code");

        using var diff = await host.Client.GetAsync("/tracon/api/workflows/coded/versions/1/diff/2");

        diff.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TraconTestHost.ReadJsonAsync(diff)).GetProperty("title").GetString().ShouldBe("Workflow has no version history");

        using var rollback = await PostRollbackAsync(host, "coded", 1);

        rollback.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TraconTestHost.ReadJsonAsync(rollback)).GetProperty("title").GetString().ShouldBe("Code-defined workflow cannot be modified");
    }

    [Fact]
    public async Task Versions_and_a_rollback_of_a_name_nothing_defines_are_plain_not_found()
    {
        await using var host = await StartAsync();

        using var listed = await host.Client.GetAsync("/tracon/api/workflows/nothing/versions");
        using var rollback = await PostRollbackAsync(host, "nothing", 1);

        listed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TraconTestHost.ReadJsonAsync(listed)).GetProperty("title").GetString().ShouldBe("Workflow not found");
        rollback.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TraconTestHost.ReadJsonAsync(rollback)).GetProperty("title").GetString().ShouldBe("Workflow not found");
    }

    [Fact]
    public async Task A_deleted_workflow_serves_no_history_but_the_numbering_continues_when_it_is_saved_again()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, Name, "one.", ["writer"]);
        await SaveAsync(host, Name, "two.", ["writer"]);
        await SaveAsync(host, Name, "three.", ["writer"]);

        using (var deleted = await host.Client.DeleteAsync($"/tracon/api/workflows/{Name}"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions");

        listed.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var again = await SaveAsync(host, Name, "four.", ["writer"]);

        again.GetProperty("version").GetInt32().ShouldBe(4, "a number is an identity: version 3 is not handed out twice");

        using var history = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions");

        (await TraconTestHost.ReadJsonAsync(history)).EnumerateArray()
            .Select(static version => version.GetProperty("version").GetInt32())
            .ShouldBe([4, 3, 2, 1]);
    }

    [Fact]
    public async Task Another_tenant_neither_reads_nor_rolls_back_this_tenants_history()
    {
        await using var host = await StartAsync(tenancy: true);

        await SaveAsync(host, Name, "alpha one.", ["writer"], tenant: "alpha");
        await SaveAsync(host, Name, "alpha two.", ["writer"], tenant: "alpha");

        using var otherList = await host.Client.SendAsync(Request(HttpMethod.Get, $"/tracon/api/workflows/{Name}/versions", "beta"));
        using var otherDiff = await host.Client.SendAsync(Request(HttpMethod.Get, $"/tracon/api/workflows/{Name}/versions/1/diff/2", "beta"));
        using var otherRollback = await PostRollbackAsync(host, Name, 1, tenant: "beta");

        otherList.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        otherDiff.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        otherRollback.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nothing leaked the other way: the first save of beta is version 1, and alpha is untouched.
        var beta = await SaveAsync(host, Name, "beta one.", ["writer"], tenant: "beta");

        beta.GetProperty("version").GetInt32().ShouldBe(1);

        using var alpha = await host.Client.SendAsync(Request(HttpMethod.Get, $"/tracon/api/workflows/{Name}/versions", "alpha"));

        (await TraconTestHost.ReadJsonAsync(alpha)).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_reader_reads_the_history_but_a_rollback_needs_the_admin_role()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseWorkflows().AddAgent(TestData.Definition("writer")),
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        // Seed through the store: the admin policy refuses the PUT, which is the point of the case.
        var store = host.Services.GetRequiredService<IWorkflowDefinitionStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await store.SaveAsync(tenant, new WorkflowDefinition { Name = Name, Kind = WorkflowKind.Sequential, AgentNames = ["writer"] });
        await store.SaveAsync(tenant, new WorkflowDefinition { Name = Name, Kind = WorkflowKind.Sequential, AgentNames = ["writer"] });

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions");
        using var diff = await host.Client.GetAsync($"/tracon/api/workflows/{Name}/versions/1/diff/2");
        using var rollback = await PostRollbackAsync(host, Name, 1);

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        diff.StatusCode.ShouldBe(HttpStatusCode.OK);
        rollback.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await store.GetAsync(tenant, Name))!.Version.ShouldBe(2, "a refused rollback wrote nothing");
    }

    [Fact]
    public async Task A_rollback_is_audited_as_workflow_rollback_with_the_versions_it_names()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, Name, "first.", ["writer"]);
        await SaveAsync(host, Name, "second.", ["writer", "editor"]);

        using var rolledBack = await PostRollbackAsync(host, Name, 1);

        rolledBack.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries = await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery
        {
            TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
            Action = "workflow.rollback",
        });

        var entry = entries.ShouldHaveSingleItem();

        entry.Entity.ShouldBe($"workflow:{Name}");
        entry.After.ShouldNotBeNull().ShouldContain("\"rolledBackToVersion\":1", Case.Sensitive);
        entry.After.ShouldContain("\"newVersion\":3", Case.Sensitive);
        entry.Before.ShouldNotBeNull().ShouldContain("\"version\":2", Case.Sensitive);
    }

    // --- Helpers ---------------------------------------------------------

    private static Task<TraconTestHost> StartAsync(bool tenancy = false)
        => TraconTestHost.StartAsync(builder =>
        {
            builder
                .AddAgent(TestData.Definition("writer"))
                .AddAgent(TestData.Definition("editor"))
                .UseWorkflows();

            if (tenancy)
            {
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            }
        });

    private static HttpRequestMessage Request(HttpMethod method, string path, string? tenant)
    {
        var request = new HttpRequestMessage(method, path);

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        return request;
    }

    private static async Task<JsonElement> SaveAsync(
        TraconTestHost host,
        string name,
        string description,
        string[] agents,
        string? tenant = null)
    {
        using var request = Request(HttpMethod.Put, $"/tracon/api/workflows/{name}", tenant);

        request.Content = JsonContent.Create(new { kind = "Sequential", description, agentNames = agents });

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static async Task<HttpResponseMessage> PostRollbackAsync(TraconTestHost host, string name, int version, string? tenant = null)
    {
        var request = Request(HttpMethod.Post, $"/tracon/api/workflows/{name}/rollback", tenant);

        request.Content = JsonContent.Create(new WorkflowRollbackRequest { Version = version });

        return await host.Client.SendAsync(request);
    }

    private static async Task<RunOutcome> RunAsync(TraconTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{name}/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var runId = JsonDocument.Parse(frames.First(static frame => string.Equals(frame.Event, "run", StringComparison.Ordinal)).Data)
            .RootElement.GetProperty("runId").GetGuid();

        var store = host.Services.GetRequiredService<IRunStore>();
        var record = (await store.GetRunAsync(runId))!;
        var children = (await store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(run => run.ParentRunId == runId)
            .Select(static run => run.AgentName)
            .ToList();

        return new RunOutcome(record, children);
    }

    private sealed record RunOutcome(RunRecord Record, IReadOnlyList<string> ChildAgents);
}
