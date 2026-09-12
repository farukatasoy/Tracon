using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Workflow endpoints: catalog, definition management, runs, and checkpoints.
/// </summary>
/// <remarks>
/// Two setups are tested: with the engine registered (<c>UseWorkflows()</c>)
/// and without. In the unregistered setup, definition management must work;
/// running must return <c>501</c>.
/// </remarks>
public sealed class WorkflowEndpointTests
{
    [Fact]
    public async Task Run_returns_501_when_engine_not_registered()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition("writer")));

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/chain/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("UseWorkflows()", Case.Sensitive);
    }

    [Fact]
    public async Task Definition_management_works_when_engine_not_registered()
    {
        // Definition stores are always registered by AddTracon(); the
        // engine only enables EXECUTION. This way, even if the engine is
        // removed during an incident, definitions remain readable.
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await SaveAsync(host, "chain", Sequential());

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var listed = await host.Client.GetAsync("/tracon/api/workflows");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(listed);

        body.EnumerateArray().Select(static item => item.GetProperty("name").GetString())
            .ShouldContain(static name => string.Equals(name, "chain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definition_is_saved_read_and_deleted()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await TraconTestHost.ReadJsonAsync(saved);

            body.GetProperty("name").GetString().ShouldBe("chain");
            body.GetProperty("version").GetInt32().ShouldBe(1);

            // Enums are written on the wire as NAMES, not numbers.
            body.GetProperty("kind").GetString().ShouldBe("Sequential");
        }

        using (var read = await host.Client.GetAsync("/tracon/api/workflows/chain"))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var deleted = await host.Client.DeleteAsync("/tracon/api/workflows/chain"))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var missing = await host.Client.GetAsync("/tracon/api/workflows/chain"))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Invalid_definition_is_rejected_AT_SAVE_TIME()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "empty", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = [],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("has no agents", Case.Sensitive);
    }

    [Fact]
    public async Task Duplicate_agent_name_is_rejected()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "duplicate", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "writer"],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Functions_endpoint_lists_registered_functions()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("writer"))
            .UseWorkflows()
            .AddWorkflowFunction<string, string>(
                "uppercase",
                static _ => (input, _, _) => new ValueTask<string>(input.ToUpperInvariant()),
                "Uppercases the input."));

        using var response = await host.Client.GetAsync("/tracon/api/workflows/functions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        var function = body.EnumerateArray().Single();

        function.GetProperty("name").GetString().ShouldBe("uppercase");
        function.GetProperty("description").GetString().ShouldBe("Uppercases the input.");
    }

    [Fact]
    public async Task Functions_endpoint_returns_an_empty_list_when_none_registered()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync("/tracon/api/workflows/functions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Definition_pointing_to_an_unregistered_function_is_rejected_AT_SAVE_TIME()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "mixed-chain", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "missing-function", Kind = WorkflowNodeKind.Function },
            ],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("'missing-function'", Case.Sensitive);
        problem.GetProperty("detail").GetString()!.ShouldContain("no such function is registered", Case.Sensitive);
    }

    [Fact]
    public async Task Mixed_node_definition_is_saved_and_read_back()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("writer"))
            .AddAgent(TestData.Definition("editor"))
            .UseWorkflows()
            .AddWorkflowFunction<string, string>(
                "uppercase",
                static _ => (input, _, _) => new ValueTask<string>(input.ToUpperInvariant())));

        using var saved = await SaveAsync(host, "mixed-chain", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ],
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var read = await host.Client.GetAsync("/tracon/api/workflows/mixed-chain");

        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(read);
        var nodes = body.GetProperty("nodes").EnumerateArray().ToList();

        nodes.Count.ShouldBe(3);
        nodes[1].GetProperty("name").GetString().ShouldBe("uppercase");
        nodes[1].GetProperty("kind").GetString().ShouldBe("Function");
    }

    [Fact]
    public async Task Mixed_chain_runs_over_SSE_end_to_end()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("writer"))
            .AddAgent(TestData.Definition("editor"))
            .UseWorkflows()
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "uppercase",
                static _ => (messages, _, _) =>
                {
                    var text = messages.LastOrDefault()?.Text ?? string.Empty;

                    return new ValueTask<List<ChatMessage>>([new ChatMessage(ChatRole.User, text.ToUpperInvariant())]);
                }));

        using (var saved = await SaveAsync(host, "mixed-chain", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ],
        }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/mixed-chain/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldContain(frame => string.Equals(frame.Event, "event", StringComparison.Ordinal) &&
                                       frame.Data.Contains("\"type\":\"ExecutorInvoked\"", StringComparison.Ordinal) &&
                                       frame.Data.Contains("\"text\":\"uppercase\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nonexistent_workflow_cannot_be_run()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/no-such-workflow/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Run_streams_over_SSE_and_produces_a_tree()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/chain/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        // The first frame reports the run id; the last frame says the
        // stream has ended. Without both, the client cannot tell whether
        // the connection dropped or the work finished.
        frames[0].Event.ShouldBe("run");
        frames[^1].Event.ShouldBe("done");

        var runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        // Tree: one workflow row + two agent rows.
        using var tree = await host.Client.GetAsync($"/tracon/api/runs/{runId}/tree");

        tree.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runs = await TraconTestHost.ReadJsonAsync(tree);
        var items = runs.EnumerateArray().ToList();

        items.Count.ShouldBe(3);

        var workflowRun = items.Single(item => string.Equals(
            item.GetProperty("kind").GetString(),
            "Workflow",
            StringComparison.Ordinal));

        workflowRun.GetProperty("workflowName").GetString().ShouldBe("chain");
        workflowRun.GetProperty("id").GetGuid().ShouldBe(runId);

        var agentRuns = items
            .Where(item => string.Equals(item.GetProperty("kind").GetString(), "Agent", StringComparison.Ordinal))
            .ToList();

        agentRuns.Count.ShouldBe(2);
        agentRuns.ShouldAllBe(item => item.GetProperty("parentRunId").GetGuid() == runId);
    }

    [Fact]
    public async Task Recorded_event_stream_uses_the_workflow_frame_names()
    {
        // Phase 145: before this phase, every one of these frames fell through
        // to the server's "unknown" branch on the RECORDED stream (this test
        // reads GET /api/runs/{id}/events, not the direct run POST stream that
        // Run_streams_over_SSE_and_produces_a_tree above already covers).
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Guid runId;

        using (var response = await host.Client.PostAsJsonAsync(
                   "/tracon/api/workflows/chain/run",
                   new WorkflowRunHttpRequest { Message = "hello" }))
        {
            var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
            runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
        }

        using var events = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);

        var recorded = await SseReader.ReadAllAsync(await events.Content.ReadAsStreamAsync());

        recorded.ShouldContain(static frame => string.Equals(frame.Event, "workflow.started", StringComparison.Ordinal));
        recorded.ShouldContain(static frame => string.Equals(frame.Event, "superstep.started", StringComparison.Ordinal));
        recorded.ShouldContain(static frame => string.Equals(frame.Event, "superstep.completed", StringComparison.Ordinal));
        recorded.ShouldContain(static frame => string.Equals(frame.Event, "executor.invoked", StringComparison.Ordinal));
        recorded.ShouldContain(static frame => string.Equals(frame.Event, "executor.completed", StringComparison.Ordinal));
        recorded.ShouldContain(static frame => string.Equals(frame.Event, "workflow.output", StringComparison.Ordinal));
        recorded.ShouldNotContain(static frame => string.Equals(frame.Event, "unknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Checkpoints_are_listed_and_resumed()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        Guid runId;

        using (var response = await host.Client.PostAsJsonAsync(
                   "/tracon/api/workflows/chain/run",
                   new WorkflowRunHttpRequest { Message = "hello" }))
        {
            var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
            runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
        }

        using (var checkpoints = await host.Client.GetAsync($"/tracon/api/workflows/runs/{runId}/checkpoints"))
        {
            checkpoints.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await TraconTestHost.ReadJsonAsync(checkpoints);

            body.GetArrayLength().ShouldBeGreaterThan(0);
        }

        using (var resumed = await host.Client.PostAsJsonAsync(
                   $"/tracon/api/workflows/runs/{runId}/resume",
                   new WorkflowResumeHttpRequest()))
        {
            resumed.StatusCode.ShouldBe(HttpStatusCode.OK);

            var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());

            frames[0].Event.ShouldBe("run");
            frames[^1].Event.ShouldBe("done");
        }
    }

    [Fact]
    public async Task Checkpoints_of_a_nonexistent_run_return_404()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync(
            $"/tracon/api/workflows/runs/{TraconId.NewId()}/checkpoints");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Phase 16: graph, pending request, response ---

    [Fact]
    public async Task Graph_endpoint_returns_nodes_edges_and_mermaid()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync("/tracon/api/workflows/chain/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var graph = await TraconTestHost.ReadJsonAsync(response);

        graph.GetProperty("name").GetString().ShouldBe("chain");
        graph.GetProperty("mermaid").GetString().ShouldNotBeNullOrWhiteSpace();
        graph.GetProperty("startExecutorId").GetString().ShouldNotBeNullOrWhiteSpace();

        var nodes = graph.GetProperty("nodes").EnumerateArray().ToList();
        var edges = graph.GetProperty("edges").EnumerateArray().ToList();

        // Two agents + the output node the built-in pattern adds.
        nodes.Count.ShouldBeGreaterThanOrEqualTo(3);
        edges.ShouldNotBeEmpty();

        // Enums are written on the wire as NAMES (K-040).
        nodes.Select(static node => node.GetProperty("kind").GetString())
            .ShouldContain(static kind => string.Equals(kind, "Agent", StringComparison.Ordinal));

        var agentNames = nodes
            .Where(static node => string.Equals(node.GetProperty("kind").GetString(), "Agent", StringComparison.Ordinal))
            .Select(static node => node.GetProperty("agentName").GetString())
            .Order(StringComparer.Ordinal)
            .ToList();

        agentNames.ShouldBe(["editor", "writer"]);
    }

    [Fact]
    public async Task Nonexistent_workflow_graph_returns_404()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync("/tracon/api/workflows/no-such-workflow/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Graph_returns_501_when_engine_not_registered()
    {
        // The graph is extracted from the COMPILED workflow; the compiler comes with the engine.
        await using var host = await TraconTestHost.StartAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync("/tracon/api/workflows/chain/graph");

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Plan_approval_is_rejected_on_another_pattern()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "chain", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
            RequirePlanApproval = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("detail").GetString()!.ShouldContain("requirePlanApproval", Case.Sensitive);
    }

    [Fact]
    public async Task Plan_approval_is_stored_in_the_definition()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await SaveAsync(host, "magentic", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Magentic,
            AgentNames = ["editor"],
            ManagerAgentName = "writer",
            RequirePlanApproval = true,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("requirePlanApproval").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_that_is_not_waiting_has_no_pending_request()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runId = await RunAsync(host, "chain");

        using var response = await host.Client.GetAsync(
            $"/tracon/api/workflows/runs/{runId}/requests");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Requests_of_a_nonexistent_run_return_404()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.GetAsync(
            $"/tracon/api/workflows/runs/{TraconId.NewId()}/requests");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Response_without_a_request_id_is_rejected()
    {
        await using var host = await StartWithEngineAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{TraconId.NewId()}/respond",
            new WorkflowRespondHttpRequest { RequestId = "  " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_response_to_a_non_waiting_run_reports_an_error_in_the_stream()
    {
        await using var host = await StartWithEngineAsync();

        using (var saved = await SaveAsync(host, "chain", Sequential()))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runId = await RunAsync(host, "chain");

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/respond",
            new WorkflowRespondHttpRequest { RequestId = "any" });

        // Once the stream has started, the status code cannot change; the
        // error is sent as an SSE frame. The same pattern is used on the
        // run endpoint.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldContain(static frame => string.Equals(frame.Event, "error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Respond_returns_501_when_engine_not_registered()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{TraconId.NewId()}/respond",
            new WorkflowRespondHttpRequest { RequestId = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    /// <summary>Runs a workflow and returns the run id.</summary>
    private static async Task<Guid> RunAsync(TraconTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{name}/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    private static Task<TraconTestHost> StartWithEngineAsync()
        => TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("writer"))
            .AddAgent(TestData.Definition("editor"))
            .UseWorkflows());

    private static Task<HttpResponseMessage> SaveAsync(
        TraconTestHost host,
        string name,
        WorkflowSaveRequest request)
        => host.Client.PutAsJsonAsync($"/tracon/api/workflows/{name}", request);

    private static WorkflowSaveRequest Sequential()
        => new()
        {
            Description = "A two-step chain.",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        };
}
