using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// 144.1's two-layer sub-agent wait limit, exercised through Microsoft Agent
/// Framework's real <c>background_agents_*</c> tool-call flow (the same rig
/// as <see cref="AgentDelegationTests"/>) rather than a hand-built
/// <see cref="ChildAgentInvoker"/> — this is the only way to prove the plain
/// agent path and the harness path apply the SAME resolved wait limits.
/// </summary>
public sealed class SubAgentTimeoutTests
{
    private const string RouterProviderName = "routing";
    private const string RouterModel = "router-model";
    private const string ResearcherProviderName = "hanging";
    private const string ResearcherModel = "researcher-model";

    // Microsoft Agent Framework's background task tools.
    private const string StartTask = "background_agents_start_task";
    private const string WaitForCompletion = "background_agents_wait_for_first_completion";
    private const string GetResults = "background_agents_get_task_results";

    [Fact]
    public async Task Cooperative_layer_cancels_a_child_that_reads_the_token()
    {
        var hanging = new HangingModelProvider(ResearcherProviderName, respectsCancellation: true);
        await using var host = await StartAsync(
            hanging, harness: false, childDeadline: TimeSpan.FromMilliseconds(50), waitTimeout: TimeSpan.FromSeconds(5));

        var agent = await ResolveRouterAsync(host);
        var response = await agent.RunAsync("start").WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldContain("did not respond in time", Case.Sensitive);

        var (types, timedOut) = await ReadRootEventsAsync(host);
        types.ShouldContain(RunEventType.ChildRunTimedOut);
        types.ShouldContain(RunEventType.ChildRunCompleted);
        timedOut.HardCutoff.ShouldBeFalse();

        // Phase 145: the recorded event stream's SSE "event:" name for this
        // frame is the stable "child.timed-out" -- before this phase it fell
        // through to "unknown" on the wire.
        var wire = await FetchRootEventStreamTextAsync(host);
        wire.ShouldContain("event: child.timed-out", Case.Sensitive);
        wire.ShouldNotContain("event: unknown", Case.Sensitive);
    }

    [Fact]
    public async Task Hard_cutoff_abandons_a_child_that_ignores_cancellation()
    {
        var hanging = new HangingModelProvider(ResearcherProviderName, respectsCancellation: false);
        await using var host = await StartAsync(
            hanging, harness: false, childDeadline: TimeSpan.FromMilliseconds(50), waitTimeout: TimeSpan.FromMilliseconds(150));

        try
        {
            var agent = await ResolveRouterAsync(host);
            var response = await agent.RunAsync("start").WaitAsync(TimeSpan.FromSeconds(10));

            response.Text.ShouldContain("did not respond in time", Case.Sensitive);

            var (types, timedOut) = await ReadRootEventsAsync(host);
            types.ShouldContain(RunEventType.ChildRunTimedOut);
            timedOut.HardCutoff.ShouldBeTrue();

            // The abandoned child is NOT reported as completed: it never
            // finished by the time the tree gave up waiting on it.
            types.ShouldNotContain(RunEventType.ChildRunCompleted);

            // The late result is discarded silently: releasing it produces no
            // further event on the root run.
            var eventCountBeforeRelease = types.Count;
            hanging.Release("late result");
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            var (typesAfterRelease, _) = await ReadRootEventsAsync(host);
            typesAfterRelease.Count.ShouldBe(eventCountBeforeRelease);
        }
        finally
        {
            // Cleanup: let the abandoned background call finish so it does not
            // outlive the test host.
            hanging.Release("late result");
        }
    }

    [Fact]
    public async Task Harness_path_produces_the_same_hard_cutoff_behavior()
    {
        var hanging = new HangingModelProvider(ResearcherProviderName, respectsCancellation: false);
        await using var host = await StartAsync(
            hanging, harness: true, childDeadline: TimeSpan.FromMilliseconds(50), waitTimeout: TimeSpan.FromMilliseconds(150));

        try
        {
            var agent = await ResolveRouterAsync(host);
            var response = await agent.RunAsync("start").WaitAsync(TimeSpan.FromSeconds(10));

            response.Text.ShouldContain("did not respond in time", Case.Sensitive);

            var (types, timedOut) = await ReadRootEventsAsync(host);
            types.ShouldContain(RunEventType.ChildRunTimedOut);
            timedOut.HardCutoff.ShouldBeTrue();
        }
        finally
        {
            hanging.Release("late result");
        }
    }

    [Fact]
    public async Task Harness_path_produces_the_same_cooperative_layer_behavior()
    {
        var hanging = new HangingModelProvider(ResearcherProviderName, respectsCancellation: true);
        await using var host = await StartAsync(
            hanging, harness: true, childDeadline: TimeSpan.FromMilliseconds(50), waitTimeout: TimeSpan.FromSeconds(5));

        var agent = await ResolveRouterAsync(host);
        var response = await agent.RunAsync("start").WaitAsync(TimeSpan.FromSeconds(10));

        response.Text.ShouldContain("did not respond in time", Case.Sensitive);

        var (types, timedOut) = await ReadRootEventsAsync(host);
        types.ShouldContain(RunEventType.ChildRunTimedOut);
        types.ShouldContain(RunEventType.ChildRunCompleted);
        timedOut.HardCutoff.ShouldBeFalse();
    }

    [Fact]
    public async Task A_wait_timeout_not_greater_than_the_child_deadline_is_rejected_when_the_agent_is_first_resolved()
    {
        var hanging = new HangingModelProvider(ResearcherProviderName, respectsCancellation: true);

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder =>
            {
                var provider = BuildRouterProvider();
                builder.AddModelProvider(provider);
                builder.AddModelProvider(hanging);

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = ResearcherBinding,
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = RouterBinding,
                    CallableAgentNames = ["arastirmaci"],
                    SubAgents = new SubAgentSettings
                    {
                        ChildDeadline = TimeSpan.FromSeconds(30),
                        WaitTimeout = TimeSpan.FromSeconds(30),
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var exception = await Should.ThrowAsync<AgentPrismCompilationException>(
            async () => await catalog.ResolveAsync("yonlendirici", culture: null, CancellationToken.None));

        exception.AgentName.ShouldBe("yonlendirici");
        exception.Message.ShouldContain(nameof(SubAgentSettings.WaitTimeout));
    }

    private static async Task<Microsoft.Agents.AI.AIAgent> ResolveRouterAsync(AgentPrismTestHost host)
    {
        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        return (await catalog.ResolveAsync("yonlendirici", culture: null, CancellationToken.None)).ShouldNotBeNull();
    }

    private static async Task<(List<RunEventType> Types, TimedOutPayload HardCutoffEvent)> ReadRootEventsAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var types = new List<RunEventType>();
        TimedOutPayload? timedOut = null;

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            types.Add(runEvent.Type);

            if (runEvent.Type == RunEventType.ChildRunTimedOut && runEvent.Payload is { } payload)
            {
                using var document = JsonDocument.Parse(payload);
                timedOut = new TimedOutPayload(document.RootElement.GetProperty("hardCutoff").GetBoolean());
            }
        }

        return (types, timedOut ?? throw new InvalidOperationException("No ChildRunTimedOut event was written."));
    }

    private static async Task<string> FetchRootEventStreamTextAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        return await host.Client.GetStringAsync(
            new Uri($"/agentprism/api/runs/{root.Id}/events", UriKind.Relative));
    }

    private static FakeModelProvider BuildRouterProvider()
    {
        var provider = new FakeModelProvider(RouterProviderName);

        provider.ForModel(RouterModel, cfg =>
        {
            cfg.CallsTool(StartTask, new { agentName = "arastirmaci", input = "sub task", description = "sub task" });

            // A real model polls wait_for_first_completion repeatedly until the
            // task settles; MAF's own wait per call is short and does not, by
            // itself, block for the whole configured WaitTimeout. Several
            // scripted polls give the hard-cutoff layer's real elapsed wait
            // time to actually pass before get_task_results is called.
            for (var poll = 0; poll < 20; poll++)
            {
                cfg.CallsTool(WaitForCompletion, new { taskIds = new[] { 1 } });
            }

            cfg.CallsTool(GetResults, new { taskId = 1 });
            cfg.EchoesLastToolResult(string.Empty, inputTokens: 4, outputTokens: 6);
        });

        return provider;
    }

    private static Task<AgentPrismTestHost> StartAsync(
        HangingModelProvider hanging, bool harness, TimeSpan childDeadline, TimeSpan waitTimeout)
        => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder =>
            {
                builder.AddModelProvider(BuildRouterProvider());
                builder.AddModelProvider(hanging);

                builder.AddAgent(new AgentDefinition
                {
                    Name = "arastirmaci",
                    Description = "Performs research.",
                    Instructions = "Research.",
                    Model = ResearcherBinding,
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = "yonlendirici",
                    Description = "Delegates the work.",
                    Instructions = "Delegate.",
                    Model = RouterBinding,
                    CallableAgentNames = ["arastirmaci"],
                    SubAgents = new SubAgentSettings { ChildDeadline = childDeadline, WaitTimeout = waitTimeout },
                    Harness = harness ? new HarnessSettings() : null,
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

    private static ModelBinding RouterBinding { get; } = new() { Provider = RouterProviderName, Model = RouterModel };

    private static ModelBinding ResearcherBinding { get; } = new() { Provider = ResearcherProviderName, Model = ResearcherModel };

    private sealed record TimedOutPayload(bool HardCutoff);
}
