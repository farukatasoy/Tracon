using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent whose definition lives in a custom <see cref="IAgentDefinitionSource"/>, run with parameter values.
/// </summary>
/// <remarks>
/// <para>
/// A source that builds its own <c>AIAgent</c> cannot receive a value: Tracon sees no schema and refuses every name
/// as unknown. A definition source hands back the definition, Tracon reads the schema from it and compiles it, so
/// the values reach the same gates, the same binding and the same record every other agent's values do.
/// </para>
/// <para>
/// One case per entry point that binds a value, through the real host: <c>/run</c>, a queued run, a batch item, a
/// replay, and a workflow step.
/// </para>
/// </remarks>
public sealed class DefinitionSourceParameterTests
{
    private const string Agent = "source-summarizer";
    private const string ProviderName = "param-provider";
    private const string ModelName = "param-model";
    private const string Instructions = "Summarize: {{articleContent}}";

    [Fact]
    public async Task A_run_binds_the_values_of_a_definition_a_source_returns_and_records_them()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRunAsync(host, Agent, Body("Body A"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe("Summarize: Body A");

        var runId = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();

        (await RecordedAsync(host, runId)).ShouldBe(Values("articleContent", "Body A"));
    }

    [Fact]
    public async Task A_name_the_sources_definition_does_not_declare_is_refused_as_unknown_before_the_model()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRunAsync(
            host,
            Agent,
            new AgentRunRequest { Message = "go", Parameters = Values("articleContent", "Body A", "artcle", "typo") });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await NamesAsync(response, "unknownParameters")).ShouldBe(["artcle"]);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_required_value_is_refused_before_the_model()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRunAsync(host, Agent, new AgentRunRequest { Message = "go" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await NamesAsync(response, "missingParameters")).ShouldBe(["articleContent"]);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_queued_run_binds_the_values_of_the_sources_definition()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{Agent}/run")
        {
            Content = JsonContent.Create(Body("Queued body")),
        };

        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        await WaitUntil.TrueAsync(() => provider.Requests.Count > 0, "the queued run to reach the model");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Summarize: Queued body");

        var runId = await WaitUntil.ValueAsync(
            async () =>
            {
                var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery());

                return runs.Count > 0 ? runs[0] : null;
            },
            static run => run is not null,
            "the queued run's row");

        (await RecordedAsync(host, runId!.Id)).ShouldBe(Values("articleContent", "Queued body"));
    }

    [Fact]
    public async Task A_batch_item_binds_its_own_values_and_one_with_a_typo_fails_alone()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var job = await RunBatchAsync(
            host,
            """{"message":"one","parameters":{"articleContent":"Item one"}}""",
            """{"message":"two","parameters":{"articleContent":"Item two","artcle":"typo"}}""",
            """{"message":"three","parameters":{"articleContent":"Item three"}}""");
        var items = job.Items;

        items.Select(static item => item.Status).ShouldBe([JobItemStatus.Completed, JobItemStatus.Failed, JobItemStatus.Completed]);
        provider.Requests.Select(static request => request.Options!.Instructions).ShouldBe(
            ["Summarize: Item one", "Summarize: Item three"]);
        (await RecordedAsync(host, items[0].RunId!.Value)).ShouldBe(Values("articleContent", "Item one"));
    }

    [Fact]
    public async Task A_replay_repeats_the_values_its_source_run_recorded()
    {
        // The definition is in no stored copy, so the replay takes the catalog path: it finds the definition
        // through the same lookup the run did, and rebuilds the agent with the recorded values.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var run = await PostRunAsync(host, Agent, Body("Body A"));

        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(run)).GetProperty("runId").GetGuid();

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{runId}/replay", new { toolMode = "LiveTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        provider.Requests.Count.ShouldBe(2);
        provider.Requests[^1].Options!.Instructions.ShouldBe("Summarize: Body A");
    }

    [Fact]
    public async Task A_workflow_step_that_names_the_sources_agent_receives_the_values_its_own_schema_declares()
    {
        var provider = Provider();
        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            builder => builder.AddAgentDefinitionSource(Source()));

        await WorkflowParameterBindingTests.SaveWorkflowAsync(
            host,
            agentNames: [Agent],
            workflowParameters: [new AgentParameter { Name = "articleContent", Kind = AgentParameterKind.Text, Required = true }],
            name: "article-flow");

        var run = await WorkflowParameterBindingTests.RunAsync(
            host,
            "article-flow",
            new WorkflowRunHttpRequest
            {
                Message = "go",
                Parameters = Values("articleContent", "Workflow body"),
            });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.Select(static request => request.Options!.Instructions).ShouldContain("Summarize: Workflow body", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_batch_where_no_item_carries_a_value_fails_the_job_once_for_the_required_parameter()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var job = await RunBatchAsync(host, "plain one", "plain two");

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldContain("'articleContent'", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    internal static AgentDefinition Definition() => new()
    {
        Name = Agent,
        Instructions = Instructions,
        Parameters = [new AgentParameter { Name = "articleContent", Kind = AgentParameterKind.Text, Required = true }],
        Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
    };

    private static InMemoryDefinitionSource Source() => new InMemoryDefinitionSource().Add(Definition());

    private static AgentRunRequest Body(string article)
        => new() { Message = "go", Parameters = Values("articleContent", article) };

    private static Dictionary<string, string> Values(params string[] pairs)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index + 1 < pairs.Length; index += 2)
        {
            values[pairs[index]] = pairs[index + 1];
        }

        return values;
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgentDefinitionSource(Source());
                builder.UseScheduling(options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                });
            });

    private static async Task<HttpResponseMessage> PostRunAsync(TraconTestHost host, string agent, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agent}/run")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }

    private static async Task<IReadOnlyDictionary<string, string>?> RecordedAsync(TraconTestHost host, Guid runId)
    {
        var record = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(
            host.Services.GetRequiredService<ITenantContext>().TenantId,
            runId,
            TestContext.Current.CancellationToken);

        return record.ShouldNotBeNull().Parameters;
    }

    private static async Task<List<string?>> NamesAsync(HttpResponseMessage response, string property)
        => [.. (await TraconTestHost.ReadJsonAsync(response)).GetProperty(property).EnumerateArray().Select(static element => element.GetString())];

    /// <summary>Saves a batch schedule over the source's agent, triggers it and waits for the job to finish.</summary>
    private static async Task<BatchOutcome> RunBatchAsync(TraconTestHost host, params string[] items)
    {
        var payload = "[" + string.Join(",", items.Select(static item => item.StartsWith('{') ? item : JsonSerializer.Serialize(item))) + "]";

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/schedules/batch",
                   new JobScheduleSaveRequest
                   {
                       HandlerKey = JobHandlerKeys.AgentBatch,
                       TargetName = Agent,
                       Payload = JsonDocument.Parse(payload).RootElement,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/batch/trigger", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
        var store = host.Services.GetRequiredService<IJobStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            async () => (await store.GetAsync(tenant, jobId))!,
            static job => job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the batch job to finish");

        return new BatchOutcome(job.Status, job.ErrorMessage, [.. (await store.ListItemsAsync(jobId)).OrderBy(static item => item.Seq)]);
    }

    private sealed record BatchOutcome(JobStatus Status, string? ErrorMessage, IReadOnlyList<JobItemRecord> Items);
}
