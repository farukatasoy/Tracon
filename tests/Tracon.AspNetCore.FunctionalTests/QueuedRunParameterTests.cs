using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued run (<c>Prefer: respond-async</c>) carries parameter values and
/// documents to the worker.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-294: the queued path refused both fields with a <c>400</c>, so a queued
/// or scheduled run could only ever bind a parameterized agent's defaults.
/// Every case goes through the real host, the real job store and the real
/// worker: the value is written into a durable payload by the endpoint and read
/// back by a handler in another scope, and a field one side writes and the
/// other does not read is a silent loss (a unit test of either half passes).
/// </para>
/// </remarks>
public sealed class QueuedRunParameterTests
{
    private const string Required = "billing-agent";
    private const string DefaultsOnly = "greeting-agent";
    private const string Plain = "plain-agent";
    private const string ModelName = "queue-model";

    [Fact]
    public async Task A_queued_run_binds_the_values_it_was_given_and_records_them()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var accepted = await PostQueuedAsync(host, Required, new AgentRunRequest
        {
            Message = "hi",
            Parameters = Values(("customer", "Acme")),
        });

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitUntil.TrueAsync(() => provider.Requests.Count > 0, "the queued run to reach the model");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");

        // The worker's run recorded the values it ran with: a replay needs them.
        var record = await RecordedAsync(host, runId);

        record.ShouldNotBeNull();
        record.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme");
    }

    [Fact]
    public async Task A_queued_run_with_a_missing_required_value_is_refused_with_the_agent_run_body()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostQueuedAsync(host, Required, new AgentRunRequest { Message = "hi" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("missingParameters").EnumerateArray().Select(static item => item.GetString())
            .ShouldBe(["customer"]);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_queued_run_refuses_an_undeclared_name_and_an_overlong_value()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var unknown = await PostQueuedAsync(host, Required, new AgentRunRequest
        {
            Message = "hi",
            Parameters = Values(("customer", "Acme"), ("custmer", "typo")),
        });

        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(unknown)).GetProperty("unknownParameters").EnumerateArray()
            .Select(static item => item.GetString()).ShouldBe(["custmer"]);

        var limit = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>()
            .Value.MaxParameterValueLength;

        using var tooLong = await PostQueuedAsync(host, Required, new AgentRunRequest
        {
            Message = "hi",
            Parameters = Values(("customer", new string('x', limit + 1))),
        });

        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(tooLong)).GetProperty("tooLongParameters").EnumerateArray()
            .Select(static item => item.GetString()).ShouldBe(["customer"]);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Values_for_an_agent_that_declares_none_are_refused()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostQueuedAsync(host, Plain, new AgentRunRequest
        {
            Message = "hi",
            Parameters = Values(("customer", "Acme")),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("unknownParameters").EnumerateArray()
            .Select(static item => item.GetString()).ShouldBe(["customer"]);
    }

    [Fact]
    public async Task A_queued_run_with_no_values_still_binds_the_defaults_and_records_them()
    {
        // The old behavior stays: no 'parameters' field means the schema's
        // defaults, bound by the catalog path. The final set is still recorded.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var accepted = await PostQueuedAsync(host, DefaultsOnly, new AgentRunRequest { Message = "hi" });

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitUntil.TrueAsync(() => provider.Requests.Count > 0, "the queued run to reach the model");

        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello friend, how can I help?");

        var record = await RecordedAsync(host, runId);

        record.ShouldNotBeNull();
        record.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "friend");
    }

    [Fact]
    public async Task A_payload_from_before_this_phase_runs_as_it_always_did()
    {
        // A schedule can name the built-in AgentRun key with a hand-written
        // payload, and an older build wrote none of the new fields.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var runId = await EnqueueRawAsync(
            host,
            DefaultsOnly,
            new { runId = "{runId}", message = "hi" });

        await WaitUntil.TrueAsync(() => provider.Requests.Count > 0, "the queued run to reach the model");
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello friend, how can I help?");

        // The worker closes the run a moment AFTER the model answered: one read
        // here raced the completion and saw Running (measured: red in isolation
        // on the base commit, green with a delay). Wait for the closing status,
        // the same way the malformed-payload cases below do.
        var status = await WaitUntil.ValueAsync(
            () => RunStatusAsync(host, runId),
            static status => status is RunStatus.Completed or RunStatus.Failed,
            "the queued run to be closed");

        status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_malformed_payload_fails_the_run_once_and_names_the_field()
    {
        // A hand-written payload with a parameter that is not a string must
        // close the queued placeholder row as Failed, not leave it Queued or
        // retry forever.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var runId = await EnqueueRawAsync(
            host,
            Required,
            new { runId = "{runId}", message = "hi", parameters = new { customer = 42 } });

        var status = await WaitUntil.ValueAsync(
            () => RunStatusAsync(host, runId),
            static status => status is RunStatus.Failed or RunStatus.Completed,
            "the malformed run to be closed");

        status.ShouldBe(RunStatus.Failed);

        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!;

        run.Error.ShouldNotBeNull().Message.ShouldContain("parameters", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_value_the_agent_no_longer_accepts_fails_the_run_at_the_worker()
    {
        // The schema is checked again where the run executes: a hand-written
        // payload (or an agent edited after the 202) can carry a name the
        // current schema does not declare.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        var runId = await EnqueueRawAsync(
            host,
            Required,
            new { runId = "{runId}", message = "hi", parameters = new { customer = "Acme", custmer = "typo" } });

        var status = await WaitUntil.ValueAsync(
            () => RunStatusAsync(host, runId),
            static status => status is RunStatus.Failed or RunStatus.Completed,
            "the run to be closed");

        status.ShouldBe(RunStatus.Failed);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_queued_documents_become_messages_before_the_request_and_leave_their_event()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var accepted = await PostQueuedAsync(host, Plain, new AgentRunRequest
        {
            Message = "summarise",
            Documents =
            [
                new AgentRunDocument { Name = "brief.txt", Content = "Ship on Friday." },
                new AgentRunDocument { Name = "notes.txt", Content = "Mind the budget." },
            ],
        });

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitUntil.TrueAsync(() => provider.Requests.Count > 0, "the queued run to reach the model");

        // Documents first, in order, then the request: the same order the
        // synchronous run builds (AgentEndpoints.BuildMessagesAsync).
        var texts = provider.Requests[^1].Messages.Select(static message => message.Text).ToList();

        texts.Count.ShouldBe(3);
        texts[0].ShouldContain("brief.txt", Case.Sensitive);
        texts[0].ShouldContain("Ship on Friday.", Case.Sensitive);
        texts[1].ShouldContain("notes.txt", Case.Sensitive);
        texts[2].ShouldBe("summarise");

        await WaitUntil.TrueAsync(
            async () => (await EventTypesAsync(host, runId)).Count(static type => type == RunEventType.DocumentAttached) == 2,
            "the DocumentAttached events");
    }

    [Fact]
    public async Task Approvals_tool_results_and_attachments_are_still_refused_on_the_queued_path()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostQueuedAsync(host, Plain, new AgentRunRequest
        {
            Message = "hi",
            AttachmentIds = [Guid.NewGuid()],
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("does not support", Case.Sensitive);
    }

    [Fact]
    public async Task Two_queued_runs_with_different_values_never_share_an_instruction()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var first = await PostQueuedAsync(host, Required, new AgentRunRequest
        {
            Message = "one",
            Parameters = Values(("customer", "Acme")),
        });

        using var second = await PostQueuedAsync(host, Required, new AgentRunRequest
        {
            Message = "two",
            Parameters = Values(("customer", "Globex")),
        });

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await WaitUntil.TrueAsync(() => provider.Requests.Count >= 2, "both queued runs to reach the model");

        provider.Requests.Select(static request => request.Options!.Instructions)
            .OrderBy(static text => text, StringComparer.Ordinal)
            .ShouldBe(["Hello Acme, how can I help?", "Hello Globex, how can I help?"]);
    }

    private static AgentDefinition Definition(string name, string instructions, params AgentParameter[] parameters) => new()
    {
        Name = name,
        Instructions = instructions,
        Parameters = parameters,
        Model = new ModelBinding { Provider = "queue-provider", Model = ModelName },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider("queue-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Dictionary<string, string> Values(params (string Name, string Value)[] pairs)
        => pairs.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(provider)
                .AddAgent(Definition(
                    Required,
                    "Hello {{customer}}, how can I help?",
                    new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }))
                .AddAgent(Definition(
                    DefaultsOnly,
                    "Hello {{customer}}, how can I help?",
                    new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" }))
                .AddAgent(Definition(Plain, "Reply briefly.")),
            configureServices: services => services.UseScheduling(options =>
            {
                options.RunWorker = true;
                options.PollInterval = TimeSpan.FromMilliseconds(20);
            }));

    private static async Task<HttpResponseMessage> PostQueuedAsync(TraconTestHost host, string agentName, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agentName}/run")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }

    /// <summary>
    /// Writes an AgentRun job the way a schedule with a hand-written payload
    /// would, with a placeholder run row so the worker's start is an upsert.
    /// </summary>
    private static async Task<Guid> EnqueueRawAsync(TraconTestHost host, string agentName, object payload)
    {
        var runId = TraconId.NewId();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var text = JsonSerializer.Serialize(payload).Replace("{runId}", runId.ToString(), StringComparison.Ordinal);
        var now = DateTimeOffset.UtcNow;

        await host.Services.GetRequiredService<IRunStore>().StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = agentName,
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = tenant,
            });

        await host.Services.GetRequiredService<IJobStore>().EnqueueAsync(
            new JobRecord
            {
                Id = runId,
                TenantId = tenant,
                HandlerKey = JobHandlerKeys.AgentRun,
                Lane = JobLanes.Default,
                TargetName = agentName,
                Status = JobStatus.Pending,
                Payload = JsonDocument.Parse(text).RootElement.Clone(),
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            CancellationToken.None);

        return runId;
    }

    private static async Task<RunStatus> RunStatusAsync(TraconTestHost host, Guid runId)
        => (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!.Status;

    private static async Task<RunInputRecord?> RecordedAsync(TraconTestHost host, Guid runId)
    {
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var inputs = host.Services.GetRequiredService<IRunInputStore>();

        // The record is written when the worker opens the run, a moment after
        // the model was reached or the 202 returned.
        return await WaitUntil.ValueAsync(
            () => inputs.GetAsync(tenant, runId).AsTask(),
            static record => record is not null,
            "the run's input record");
    }

    private static async Task<List<RunEventType>> EventTypesAsync(TraconTestHost host, Guid runId)
    {
        var types = new List<RunEventType>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(runId))
        {
            types.Add(runEvent.Type);
        }

        return types;
    }
}
