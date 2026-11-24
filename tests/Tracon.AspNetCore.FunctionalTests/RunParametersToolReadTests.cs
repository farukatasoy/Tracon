using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool of an agent run reads the parameter values the run was bound with, and who supplied them.
/// </summary>
/// <remarks>
/// <para>
/// The values fill the placeholders of the instructions, and a tool of the same run often needs to know what the run was started
/// for. The only way to read them used to be to write them into the thread's identity. A run keeps them on its scope now
/// (<c>AgentRunScope.Parameters</c>) with their source (<c>AgentRunScope.ParameterSource</c>), and a tool reads both.
/// </para>
/// <para>
/// Each surface that starts or repeats a run is a case of its own, because each builds the run's options in another place: a
/// plain run, a streamed one, a queued one, an approval resumed from the inbox, a replay, and an embedding caller's own call. The
/// source of a value a surface took from a request, or kept from one, is <see cref="ParameterValueSource.Client"/>: the source is
/// not stored with the values, and a repeated run does not know who wrote them.
/// </para>
/// </remarks>
public sealed class RunParametersToolReadTests
{
    private const string Agent = "context-agent";
    // No hyphen: the tool a calling agent sees for a callee carries the callee's name, and a function name has none.
    private const string ChildAgent = "helper";
    private const string Provider = "context-provider";
    private const string ModelName = "context-model";
    private const string Probe = "probe_context";
    private const string SessionId = "context-session";

    [Fact]
    public async Task A_tool_of_a_plain_run_reads_the_values_with_the_defaults_and_as_the_clients()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe);

        using var response = await PostBufferedAsync(host, Agent, Body(("customer", "Acme")));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var read = probe.Reads.ShouldHaveSingleItem();

        read.Depth.ShouldBe(0);
        read.Parameters.ShouldNotBeNull().ShouldBe(Expected("Acme", "plain"), ignoreOrder: true);
        read.Source.ShouldBe(ParameterValueSource.Client);
    }

    [Fact]
    public async Task A_tool_of_a_streamed_run_reads_them_too()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe);

        using var response = await host.Client.PostAsJsonAsync($"/tracon/api/agents/{Agent}/run", Body(("customer", "Acme"), ("tone", "formal")));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var read = probe.Reads.ShouldHaveSingleItem();

        read.Parameters.ShouldNotBeNull().ShouldBe(Expected("Acme", "formal"), ignoreOrder: true);
        read.Source.ShouldBe(ParameterValueSource.Client);
    }

    [Fact]
    public async Task A_tool_of_a_queued_run_reads_the_values_the_worker_ran_with()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe, scheduling: true);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        var read = probe.Reads.ShouldHaveSingleItem();

        read.Parameters.ShouldNotBeNull().ShouldBe(Expected("Acme", "plain"), ignoreOrder: true);
        read.Source.ShouldBe(ParameterValueSource.Client);
    }

    [Fact]
    public async Task A_tool_of_a_replay_reads_the_values_of_the_run_it_repeats()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe, recordRunInput: true, rounds: 2);

        using var first = await PostBufferedAsync(host, Agent, Body(("customer", "Acme"), ("tone", "formal")));
        var sourceId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("runId").GetGuid();

        probe.Reads.Count.ShouldBe(1);

        using var replay = await host.Client.PostAsJsonAsync($"/tracon/api/runs/{sourceId}/replay", new { toolMode = "LiveTools" });

        replay.StatusCode.ShouldBe(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());

        // The replay ran the tool again, under the values of the run it repeats. It does not know who wrote them.
        probe.Reads.Count.ShouldBe(2);
        probe.Reads[1].Parameters.ShouldNotBeNull().ShouldBe(Expected("Acme", "formal"), ignoreOrder: true);
        probe.Reads[1].Source.ShouldBe(ParameterValueSource.Client);
    }

    [Fact]
    public async Task A_tool_of_a_run_resumed_by_an_approval_reads_the_values_of_the_run_that_asked()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe, recordRunInput: true, scheduling: true, flagged: true);

        var sourceRunId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(sourceRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");
        probe.Reads.ShouldBeEmpty("the tool does not run before the approval");

        using var pending = await host.Client.GetAsync("/tracon/api/approvals/pending");
        var approvalId = (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        using var decided = await host.Client.PostAsJsonAsync($"/tracon/api/approvals/{approvalId}/decide", new ApprovalDecisionRequest { Approved = true });

        decided.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitUntil.TrueAsync(() => probe.Reads.Count == 1, "the approved tool to run in the resumed run");

        var read = probe.Reads[0];

        read.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme");
        read.Source.ShouldBe(ParameterValueSource.Client);
    }

    [Fact]
    public async Task An_embedding_callers_own_values_read_as_the_callers()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync(
            Agent,
            new AgentResolveOptions { Parameters = Body(("customer", "Acme")).Parameters },
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

        _ = await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        var read = probe.Reads.ShouldHaveSingleItem();

        read.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme");
        read.Source.ShouldBe(ParameterValueSource.Caller);
    }

    [Fact]
    public async Task A_sub_agent_the_run_calls_reads_none()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe, withChild: true);

        using var response = await PostBufferedAsync(host, Agent, Body(("customer", "Acme")));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        probe.Reads.Count.ShouldBe(2, $"the root's tool and the sub-agent's tool; depths read: [{string.Join(",", probe.Reads.Select(static read => read.Depth))}]");

        var root = probe.Reads.Single(static read => read.Depth == 0);
        var child = probe.Reads.Single(static read => read.Depth == 1);

        root.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme");
        child.Parameters.ShouldBeNull("a sub-agent receives none of its caller's values");
    }

    [Fact]
    public async Task An_agent_that_declares_no_parameters_reads_none()
    {
        var probe = new ScopeProbe();
        await using var host = await StartAsync(probe);

        using var response = await PostBufferedAsync(host, ChildAgent, new AgentRunRequest { Message = "hi" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        probe.Reads.ShouldHaveSingleItem().Parameters.ShouldBeNull();
    }

    private static AgentRunRequest Body(params (string Name, string Value)[] values)
        => new()
        {
            Message = "hi",
            SessionId = SessionId,
            Parameters = values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal),
        };

    private static Dictionary<string, string> Expected(string customer, string tone)
        => new(StringComparer.Ordinal) { ["customer"] = customer, ["tone"] = tone };

    private static async Task<TraconTestHost> StartAsync(
        ScopeProbe probe,
        bool scheduling = false,
        bool recordRunInput = false,
        bool flagged = false,
        bool withChild = false,
        int rounds = 1)
    {
        var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(Script(withChild, rounds))
                    .AddTool(
                        (Func<string>)(() =>
                        {
                            probe.Read();

                            return "read";
                        }),
                        name: Probe,
                        description: "Reads the run's parameters the way a tool of the host would.",
                        configure: options => options.RequiresApproval = flagged);

                if (scheduling)
                {
                    builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                }
            },
            configureServices: services => services.Configure<TraconOptions>(options => options.RunRecording.RecordRunInput = recordRunInput));

        var binding = new ModelBinding { Provider = Provider, Model = ModelName };

        // The callee first: a definition that names a callable agent is refused until that agent exists.
        using (var child = await host.Client.PostAsJsonAsync(
                   "/tracon/api/agents",
                   new AgentDefinitionRequest
                   {
                       Name = ChildAgent,
                       Description = "Helps with a sub task.",
                       Instructions = "Help.",
                       Model = new ModelBinding { Provider = Provider, Model = withChild ? "child-model" : ModelName },
                       ToolNames = [Probe],
                   }))
        {
            child.EnsureSuccessStatusCode();
        }

        using var created = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            new AgentDefinitionRequest
            {
                Name = Agent,
                Description = "Answers the customer.",
                Instructions = "Hello {{customer}}, answer in a {{tone}} tone.",
                Parameters =
                [
                    new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                    new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
                ],
                Model = binding,
                ToolNames = [Probe],
                CallableAgents = withChild ? [ChildAgent] : [],
                SubAgents = withChild ? new SubAgentSettings { CallMode = SubAgentCallMode.Tool } : null,
            });

        created.EnsureSuccessStatusCode();

        return host;
    }

    private static FakeModelProvider Script(bool withChild, int rounds)
    {
        // The two-agent script names its models with ForModel, which is the way the provider tells one agent's model from the
        // other's; the single-agent one has one model, which it declares.
        var provider = new FakeModelProvider(Provider);

        if (!withChild)
        {
            provider.WithModel(new ModelDescriptor { Name = ModelName });

            // The script is one queue for the whole provider: a run that repeats another needs a round of its own.
            for (var round = 1; round < rounds; round++)
            {
                provider.CallsTool(Probe, new { }).RespondsWith("done");
            }

            return provider.CallsTool(Probe, new { }).EchoesLastToolResult();
        }

        // The root asks its own tool for the values, then calls the sub-agent, whose own model asks the same tool.
        provider.ForModel(ModelName, root => root
            .CallsTool(Probe, new { })
            .CallsTool(ChildAgent, new { query = "help" })
            .EchoesLastToolResult("Delegated: "));
        provider.ForModel("child-model", child => child.CallsTool(Probe, new { }).EchoesLastToolResult("Helper: "));

        return provider;
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, string agent, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agent}/run") { Content = JsonContent.Create(body) };

        // An Idempotency-Key makes the run non-streaming: one JSON body, with the run id in it.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{Agent}/run")
        {
            Content = JsonContent.Create(Body(("customer", "Acme"))),
        };

        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    /// <summary>What a tool of the run sees in its scope. One per test, so parallel tests do not share it.</summary>
    private sealed class ScopeProbe
    {
        private readonly List<ScopeRead> _reads = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<ScopeRead> Reads
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reads];
                }
            }
        }

        public void Read()
        {
            var scope = TraconRunContext.Current;

            lock (_gate)
            {
                _reads.Add(new ScopeRead(scope?.Depth ?? -1, scope?.Parameters, scope?.ParameterSource ?? ParameterValueSource.Client));
            }
        }
    }

    private sealed record ScopeRead(int Depth, IReadOnlyDictionary<string, string>? Parameters, ParameterValueSource Source);
}
