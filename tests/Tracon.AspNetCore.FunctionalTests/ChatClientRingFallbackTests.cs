using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The fallback chain, the circuit and error normalization sit above an extension's
/// ring. A provider failure passes up through the ring; the fallback link that answers instead
/// gets its own instance of the same ring; the failure that ends a run is masked after the ring
/// saw it.
/// </summary>
/// <remarks>
/// A fallback link is built when it is first used, from a binding of its own. A compliance ring
/// that only the primary pipeline took would not see the calls of the model that answered.
/// </remarks>
public sealed class ChatClientRingFallbackTests
{
    private const string AgentName = "fallback-ring-agent";
    private const string PrimaryProvider = "ring-primary";
    private const string FallbackProvider = "ring-fallback";
    private const string UpstreamFailure = "HTTP 503 (Service Unavailable) LEAK-MARKER-503";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task The_fallback_link_runs_the_same_rings_and_the_rings_of_the_primary_saw_its_failure()
    {
        var log = new RingLog();
        var fallback = new FakeModelProvider(FallbackProvider).ForModel("fallback-1", static model => model.RespondsWith("from the fallback"));

        await using var host = await StartAsync(fallback, log, withFallback: true);

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("from the fallback");

        // Each pipeline built its own ring: one for the primary at compile time, one for the link.
        log.Of("model-call-1").ShouldHaveSingleItem().Failure.ShouldNotBeNull();
        log.Of("turn-1").ShouldHaveSingleItem().Failure.ShouldNotBeNull();
        log.Of("model-call-2").ShouldHaveSingleItem().Failure.ShouldBeNull();
        log.Of("turn-2").ShouldHaveSingleItem().Failure.ShouldBeNull();
        fallback.Requests.ShouldHaveSingleItem();

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(run.Id, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(runEvent);
        }

        events.ShouldContain(static runEvent => runEvent.Type == RunEventType.ModelFallbackUsed);
    }

    [Fact]
    public async Task A_provider_failure_that_passed_up_through_a_ring_is_still_masked()
    {
        var log = new RingLog();

        await using var host = await StartAsync(fallback: null, log, withFallback: false);

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("event: error");
        body.ShouldNotContain("LEAK-MARKER-503");
        log.Of("model-call-1").ShouldHaveSingleItem().Failure.ShouldNotBeNull();

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldNotContain("LEAK-MARKER-503");
    }

    /// <summary>
    /// A ring's own exception is the host's, like one from a wrapper that <c>context.Use</c> adds:
    /// it is not a provider failure, so the fallback chain does not try another model for it.
    /// </summary>
    [Fact]
    public async Task A_rings_own_failure_fails_the_run_and_does_not_move_to_the_fallback()
    {
        var fallback = new FakeModelProvider(FallbackProvider).ForModel("fallback-1", static model => model.RespondsWith("from the fallback"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider(PrimaryProvider).EchoesUserMessage())
            .AddModelProvider(fallback)
            .AddAgentExtension("failing", static context => context.UseChatClient(ChatClientSlot.ModelCall, static inner => new FailingRing(inner)))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Answer.",
                Model = new ModelBinding
                {
                    Provider = PrimaryProvider,
                    Model = "primary-1",
                    Fallbacks = [new ModelFallback { Provider = FallbackProvider, Model = "fallback-1" }],
                },
                ExtensionNames = ["failing"],
                Origin = AgentDefinitionOrigin.Code,
            }));

        using var response = await host.Client.PostAsJsonAsync(RunUri, new AgentRunRequest { Message = "hello" });
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("event: error");
        fallback.Requests.ShouldBeEmpty();

        var run = (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
    }

    /// <summary>
    /// The circuit sits above both slots. A provider failure that passes up through a ring counts,
    /// so with a threshold of one the next turn stops at the open circuit and never reaches the
    /// ring. A ring's own failure does not count, so the next turn reaches the ring again.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_a_provider_failure_that_passed_up_through_a_ring_opens_the_circuit(bool providerFails)
    {
        var log = new RingLog();
        var provider = providerFails ? "circuit-broken" : "circuit-healthy";

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(providerFails
                    ? new ThrowingModelProvider(provider, UpstreamFailure)
                    : new FakeModelProvider(provider).EchoesUserMessage());

                builder
                    .AddAgentExtension("ring", context => context.UseChatClient(
                        ChatClientSlot.Turn,
                        inner => providerFails
                            ? new RecordingChatClientRing(inner, "turn", log)
                            : new FailingRing(inner, log)))
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Answer.",
                        Model = new ModelBinding { Provider = provider, Model = "kirik-1" },
                        ExtensionNames = ["ring"],
                        Origin = AgentDefinitionOrigin.Code,
                    });
            },
            configureServices: static services => services.Configure<TraconOptions>(static options =>
            {
                options.CircuitBreaker.FailureThreshold = 1;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            }));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        await Should.ThrowAsync<Exception>(() => agent.RunAsync("first", cancellationToken: TestContext.Current.CancellationToken));
        var second = await Should.ThrowAsync<Exception>(() => agent.RunAsync("second", cancellationToken: TestContext.Current.CancellationToken));

        if (providerFails)
        {
            // The open circuit refused the second turn above the ring.
            log.Entries.ShouldBe(["turn"]);
            second.ShouldBeOfType<TraconProviderUnavailableException>();
        }
        else
        {
            // The ring threw both times: its own failure left the circuit closed.
            log.Entries.ShouldBe(["failing", "failing"]);
            second.ShouldBeOfType<InvalidOperationException>();
        }
    }

    private static Task<TraconTestHost> StartAsync(FakeModelProvider? fallback, RingLog log, bool withFallback)
    {
        var modelCallRings = 0;
        var turnRings = 0;

        return TraconTestHost.StartAsync(builder =>
        {
            builder.AddModelProvider(new ThrowingModelProvider(PrimaryProvider, UpstreamFailure));

            if (fallback is not null)
            {
                builder.AddModelProvider(fallback);
            }

            builder
                .AddAgentExtension("rings", context =>
                {
                    context.UseChatClient(
                        ChatClientSlot.ModelCall,
                        inner => new RecordingChatClientRing(inner, $"model-call-{Interlocked.Increment(ref modelCallRings)}", log));
                    context.UseChatClient(
                        ChatClientSlot.Turn,
                        inner => new RecordingChatClientRing(inner, $"turn-{Interlocked.Increment(ref turnRings)}", log));
                })
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer.",
                    Model = new ModelBinding
                    {
                        Provider = PrimaryProvider,
                        Model = "kirik-1",
                        Fallbacks = withFallback ? [new ModelFallback { Provider = FallbackProvider, Model = "fallback-1" }] : [],
                    },
                    ExtensionNames = ["rings"],
                    Origin = AgentDefinitionOrigin.Code,
                });
        });
    }

    private sealed class FailingRing(IChatClient inner, RingLog? log = null) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            log?.Enter("failing");
            throw new InvalidOperationException("The ring refused the call.");
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            log?.Enter("failing");
            throw new InvalidOperationException("The ring refused the call.");
        }
    }
}
