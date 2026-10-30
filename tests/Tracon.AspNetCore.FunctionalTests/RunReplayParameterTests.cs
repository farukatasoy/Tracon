using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A replay repeats a parameterized run with the values the source run was bound with.
/// </summary>
/// <remarks>
/// <para>
/// Replay refused every agent that declares
/// parameters, because a run recorded no values and a replay on the defaults
/// would run different instructions. A run now records the values it was bound
/// with, so a replay repeats them, with the caller's overrides laid over them.
/// A run that recorded none stays refused - NULL ("nothing was recorded") is not
/// "no values".
/// </para>
/// </remarks>
public sealed class RunReplayParameterTests
{
    private const string Agent = "billing-agent";
    private const string ModelName = "replay-model";

    [Fact]
    public async Task A_replay_repeats_the_source_runs_values_and_records_them_again()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        var sourceId = await RunAsync(host, ("customer", "Acme"), ("tone", "formal"));

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a formal tone.");

        var calls = provider.Requests.Count;
        var replay = await ReplayAsync(host, sourceId, new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(calls + 1);
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a formal tone.");

        var replayId = (await TraconTestHost.ReadJsonAsync(replay)).GetProperty("runId").GetGuid();
        var record = await RecordedAsync(host, replayId);

        record.ShouldNotBeNull();
        record.Parameters.ShouldNotBeNull();
        record.Parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ShouldBe(["customer=Acme", "tone=formal"]);
    }

    [Fact]
    public async Task A_replay_with_overrides_changes_only_the_names_it_gives()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        var sourceId = await RunAsync(host, ("customer", "Acme"), ("tone", "formal"));

        var replay = await ReplayAsync(host, sourceId, new { toolMode = "NoTools", parameters = new { tone = "casual" } });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a casual tone.");

        var replayId = (await TraconTestHost.ReadJsonAsync(replay)).GetProperty("runId").GetGuid();

        (await RecordedAsync(host, replayId))!.Parameters!["tone"].ShouldBe("casual");
        (await RecordedAsync(host, replayId))!.Parameters!["customer"].ShouldBe("Acme");
    }

    [Fact]
    public async Task An_override_the_schema_does_not_declare_is_refused_not_dropped()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        var sourceId = await RunAsync(host, ("customer", "Acme"));
        var calls = provider.Requests.Count;

        var replay = await ReplayAsync(host, sourceId, new { toolMode = "NoTools", parameters = new { tonne = "typo" } });

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await replay.Content.ReadAsStringAsync()).ShouldContain("tonne", Case.Sensitive);
        provider.Requests.Count.ShouldBe(calls);
    }

    [Fact]
    public async Task A_replay_of_a_run_with_no_recorded_input_at_all_is_not_found_and_calls_no_model()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: false);

        var sourceId = await RunAsync(host, ("customer", "Acme"));
        var calls = provider.Requests.Count;

        // Without a run_inputs row the input itself is gone, and so is the replay.
        var replay = await ReplayAsync(host, sourceId, new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        provider.Requests.Count.ShouldBe(calls);
    }

    [Fact]
    public async Task A_row_from_before_parameter_recording_is_refused_not_replayed_on_the_defaults()
    {
        // A run_inputs row with messages and NO parameter record: what every
        // parameterized run left behind before this change.
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var oldRunId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = oldRunId,
            AgentName = Agent,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
        });

        await host.Services.GetRequiredService<IRunInputStore>().SaveAsync(new RunInputRecord
        {
            RunId = oldRunId,
            TenantId = tenant,
            Messages = [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hi")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var replay = await ReplayAsync(host, oldRunId, new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = await replay.Content.ReadAsStringAsync();

        detail.ShouldContain("did not record the values", Case.Sensitive);
        detail.ShouldContain("RecordRunInput", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_replay_of_a_default_only_run_repeats_the_defaults_it_ran_with()
    {
        // The source run supplied nothing; its record holds the defaults it
        // was bound with, so the replay is not refused for want of values.
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        var sourceId = await RunAsync(host, ("customer", "Acme"));

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a plain tone.");

        var replay = await ReplayAsync(host, sourceId, new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, answer in a plain tone.");
    }

    [Fact]
    public async Task A_replay_still_works_for_a_definition_with_no_parameters()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true);

        using var run = await PostBufferedAsync(host, "plain-agent", new AgentRunRequest { Message = "hi" });

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(run)).GetProperty("runId").GetGuid();
        var replay = await ReplayAsync(host, runId, new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());

        // Override values for an agent that declares none have nowhere to go.
        var refused = await ReplayAsync(host, runId, new { toolMode = "NoTools", parameters = new { tone = "x" } });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_workflow_run_is_continued_from_its_checkpoints_not_replayed()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, recordRunInput: true, withWorkflows: true);

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/chain",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Sequential,
                       AgentNames = [Agent],
                       Parameters =
                       [
                           new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                       ],
                   }))
        {
            saved.EnsureSuccessStatusCode();
        }

        var run = await WorkflowParameterBindingTests.RunAsync(host, "chain", new WorkflowRunHttpRequest
        {
            Message = "hi",
            Parameters = WorkflowParameterBindingTests.Values(("customer", "Acme")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var replay = await ReplayAsync(host, run.Id, new { toolMode = "NoTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await replay.Content.ReadAsStringAsync()).ShouldContain("workflow run", Case.Sensitive);
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("replay-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static async Task<TraconTestHost> StartAsync(FakeModelProvider provider, bool recordRunInput, bool withWorkflows = false)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);

                if (withWorkflows)
                {
                    builder.UseWorkflows();
                }
            },
            configureServices: services => services.Configure<TraconOptions>(
                options => options.RunRecording.RecordRunInput = recordRunInput));

        var binding = new ModelBinding { Provider = "replay-provider", Model = ModelName };

        using (var created = await host.Client.PostAsJsonAsync(
                   "/tracon/api/agents",
                   new AgentDefinitionRequest
                   {
                       Name = Agent,
                       Instructions = "Hello {{customer}}, answer in a {{tone}} tone.",
                       Parameters =
                       [
                           new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                           new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
                       ],
                       Model = binding,
                   }))
        {
            created.EnsureSuccessStatusCode();
        }

        using var plain = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            new AgentDefinitionRequest { Name = "plain-agent", Instructions = "Be plain.", Model = binding });

        plain.EnsureSuccessStatusCode();

        return host;
    }

    private static async Task<Guid> RunAsync(TraconTestHost host, params (string Name, string Value)[] values)
    {
        using var run = await PostBufferedAsync(
            host,
            Agent,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal),
            });

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(run)).GetProperty("runId").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, string agent, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agent}/run")
        {
            Content = JsonContent.Create(body),
        };

        // An Idempotency-Key makes the run non-streaming (one JSON body).
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ReplayAsync(TraconTestHost host, Guid runId, object body)
        => host.Client.PostAsJsonAsync($"/tracon/api/runs/{runId}/replay", body);

    private static async Task<RunInputRecord?> RecordedAsync(TraconTestHost host, Guid runId)
        => await host.Services.GetRequiredService<IRunInputStore>()
            .GetAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, runId);
}
