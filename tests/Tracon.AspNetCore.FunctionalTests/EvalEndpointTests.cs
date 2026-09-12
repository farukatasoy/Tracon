using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>In-memory behavior tests for the eval suite/case/run endpoints (Phase 18).</summary>
public sealed class EvalEndpointTests
{
    private static readonly Uri Suites = new("/tracon/api/evals", UriKind.Relative);
    private static readonly Uri Suite = new("/tracon/api/evals/customer-support-suite", UriKind.Relative);
    private static readonly Uri Cases = new("/tracon/api/evals/customer-support-suite/cases", UriKind.Relative);
    private static readonly Uri Run = new("/tracon/api/evals/customer-support-suite/run", UriKind.Relative);
    private static readonly Uri Runs = new("/tracon/api/evals/customer-support-suite/runs", UriKind.Relative);

    [Fact]
    public async Task Suite_is_created_updated_and_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Suite, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await TraconTestHost.ReadJsonAsync(created);
            body.GetProperty("agentName").GetString().ShouldBe("customer-support-agent");
        }

        using (var updated = await host.Client.PutAsJsonAsync(Suite, Request() with { Description = "updated" }))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await TraconTestHost.ReadJsonAsync(updated)).GetProperty("description").GetString()
                .ShouldBe("updated");
        }

        using (var listed = await host.Client.GetAsync(Suites))
        {
            (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using var deleted = await host.Client.DeleteAsync(Suite);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getAfterDelete = await host.Client.GetAsync(Suite);
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_check_kind_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Suite,
            Request() with { Checks = JsonDocument.Parse("""[{"kind":"noSuchThing"}]""").RootElement });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_agent_name_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Suite, Request() with { AgentName = " " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Nonexistent_suite_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/evals/none", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cases_are_replaced_and_cleared()
    {
        await using var host = await TraconTestHost.StartAsync();
        await host.Client.PutAsJsonAsync(Suite, Request());

        using (var saved = await host.Client.PutAsJsonAsync(
                   Cases,
                   new object[]
                   {
                       new { query = "first question", expectedOutput = "expected" },
                       new { query = "second question", expectedTools = new[] { "get_order_status" } },
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await TraconTestHost.ReadJsonAsync(saved);
            body.GetArrayLength().ShouldBe(2);
            body[0].GetProperty("seq").GetInt32().ShouldBe(0);
            body[1].GetProperty("expectedTools")[0].GetString().ShouldBe("get_order_status");
        }

        using (var listed = await host.Client.GetAsync(Cases))
        {
            (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(2);
        }

        using var cleared = await host.Client.DeleteAsync(Cases);
        cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var listedAfterClear = await host.Client.GetAsync(Cases);
        (await TraconTestHost.ReadJsonAsync(listedAfterClear)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Suite_without_cases_cannot_be_run()
    {
        await using var host = await TraconTestHost.StartAsync();
        await host.Client.PutAsJsonAsync(Suite, Request());

        using var response = await host.Client.PostAsJsonAsync(Run, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Run_is_triggered_produces_a_job_and_can_be_listed()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "question" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var run = await TraconTestHost.ReadJsonAsync(triggered);
        run.GetProperty("status").GetString().ShouldBe("Pending");
        run.GetProperty("total").GetInt32().ShouldBe(1);
        var runId = run.GetProperty("id").GetGuid();
        var jobId = run.GetProperty("jobId").GetGuid();

        using var job = await host.Client.GetAsync(new Uri($"/tracon/api/jobs/{jobId}", UriKind.Relative));
        job.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(job)).GetProperty("job").GetProperty("handlerKey").GetString()
            .ShouldBe(JobHandlerKeys.Eval);

        using var listed = await host.Client.GetAsync(Runs);
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);

        using var detail = await host.Client.GetAsync(new Uri($"/tracon/api/evals/runs/{runId}", UriKind.Relative));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await TraconTestHost.ReadJsonAsync(detail);
        body.GetProperty("run").GetProperty("id").GetGuid().ShouldBe(runId);
        body.GetProperty("results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Nonexistent_run_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/evals/runs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operator_can_trigger_but_cannot_save_suite()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        // The suite bypasses HTTP authorization and is written directly to
        // the store: in this test the Admin endpoint is closed, so the
        // suite is seeded another way.
        var evalStore = host.Services.GetRequiredService<IEvalStore>();
        var suite = await evalStore.SaveSuiteAsync(new EvalSuite
        {
            TenantId = "default",
            Name = "customer-support-suite",
            AgentName = "customer-support-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
        });
        await evalStore.ReplaceCasesAsync(suite.Id, [new EvalCase { SuiteId = suite.Id, Seq = 0, Query = "question" }]);

        using var trigger = await host.Client.PostAsJsonAsync(Run, new { });
        trigger.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var save = await host.Client.PutAsJsonAsync(Suite, Request());
        save.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- Version selection (Phase 19, open question 2) ---

    [Fact]
    public async Task Run_is_triggered_with_a_specific_version()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "question" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { agentVersion = 1 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Run_with_a_nonexistent_version_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await CreateVersionedAgentAsync(host);
        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "question" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { agentVersion = 99 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Version_cannot_be_selected_for_a_code_based_agent()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition(name: "customer-support-agent")),
            configureServices: static services => services.UseScheduling(o => o.RunWorker = false));

        await host.Client.PutAsJsonAsync(Suite, Request());
        await host.Client.PutAsJsonAsync(Cases, new object[] { new { query = "question" } });

        using var triggered = await host.Client.PostAsJsonAsync(Run, new { agentVersion = 1 });

        triggered.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await triggered.Content.ReadAsStringAsync()).ShouldContain("version history");
    }

    /// <summary>Creates a database agent named "customer-support-agent" with two versions.</summary>
    private static async Task CreateVersionedAgentAsync(TraconTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request(name: "customer-support-agent", instructions: "first"));
        created.EnsureSuccessStatusCode();

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/customer-support-agent", UriKind.Relative),
            TestData.Request(name: "customer-support-agent", instructions: "second"));
        updated.EnsureSuccessStatusCode();
    }

    private static EvalSuiteSaveRequest Request()
        => new()
        {
            AgentName = "customer-support-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
        };
}
