using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Testing;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Bounded structured-response repair (docs/134): the crossings a unit test
/// cannot prove on its own — the token budget, cancellation, the fallback
/// chain, and the durable conversation history — all through a real
/// DI-built host. The repair loop's own mechanics (call count, message
/// construction, the attempt cap, event numbering) are covered at unit level
/// in <c>Tracon.Core.UnitTests.Compilation.StructuredResponseValidatingAgentTests</c>.
/// </summary>
public sealed class StructuredResponseRepairEndpointTests
{
    private const string AgentName = "structured-repair-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task MaxRepairAttempts_unset_defaults_to_zero_and_behaves_exactly_like_no_repair()
    {
        var provider = new FakeModelProvider("structured")
            .RespondsWith("not valid json at all", inputTokens: 12, outputTokens: 3);

        await using var host = await StartAsync(provider, maxRepairAttempts: null);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);
        provider.Requests.Count.ShouldBe(1);

        // The single (rejected) call's tokens were genuinely spent, and the
        // side channel that folds a discarded attempt's usage into the run
        // (134.1) runs on every rejection, not only when a repair follows -
        // this is an intentional improvement over Phase 131, where a
        // rejected run's Usage was silently null even though real tokens
        // were spent.
        run.Usage.ShouldNotBeNull();
        run.Usage!.InputTokens.ShouldBe(12);
        run.Usage.OutputTokens.ShouldBe(3);

        (await EventTypesAsync(host, run.Id)).ShouldNotContain("StructuredResponseRepairAttempted");
    }

    [Fact]
    public async Task A_repair_turn_recovers_an_invalid_response_and_the_runs_usage_is_the_sum_of_both_turns()
    {
        var provider = new FakeModelProvider("structured")
            .RespondsWith("not valid json", inputTokens: 20, outputTokens: 5)
            .RespondsWith("{\"answer\":42}", inputTokens: 8, outputTokens: 2);

        await using var host = await StartAsync(provider, maxRepairAttempts: 2);

        using var response = await PostBufferedAsync(host);

        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        provider.Requests.Count.ShouldBe(2);

        // Neither turn's token count was lost, and the repaired turn's usage
        // was not counted twice: the total is the plain sum of both.
        run.Usage.ShouldNotBeNull();
        run.Usage!.InputTokens.ShouldBe(28);
        run.Usage.OutputTokens.ShouldBe(7);

        var events = await EventTypesAsync(host, run.Id);
        events.ShouldContain("StructuredResponseRejected");
        events.ShouldContain("StructuredResponseRepairAttempted");
    }

    [Fact]
    public async Task Repair_attempts_are_capped_then_the_run_fails_exactly_like_an_unrepaired_rejection()
    {
        var provider = new FakeModelProvider("structured")
            .RespondsWith("not valid json", "still not valid json", "still not valid json");

        await using var host = await StartAsync(provider, maxRepairAttempts: 2);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        // No new RunErrorClass member was introduced for an exhausted repair
        // budget - the run fails with the SAME class a straight rejection does.
        run.Error!.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);

        // MaxRepairAttempts=2 permits at most THREE model calls: the original turn plus two repairs.
        provider.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_trees_token_budget_still_applies_to_a_repair_turn_and_stops_it_from_looping()
    {
        var provider = new FakeModelProvider("structured")
            .RespondsWith("not valid json", inputTokens: 40, outputTokens: 10)
            .RespondsWith("{\"answer\":42}");

        await using var host = await StartAsync(
            provider,
            maxRepairAttempts: 2,
            configureServices: services =>
                services.Configure<TraconOptions>(o => o.AgentGraph.MaxTotalTokens = 45));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        // The budget check runs BEFORE the model is called: the first (real)
        // turn already spends 50 tokens against a cap of 45, so the repair
        // turn is blocked before it ever reaches the provider - never the
        // two full repair rounds MaxRepairAttempts would otherwise allow.
        run.Error!.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_trees_deadline_still_applies_to_a_repair_turn_and_stops_it_from_looping()
    {
        var clock = new ManualTimeProvider();
        var provider = new ClockAdvancingModelProvider(clock, TimeSpan.FromSeconds(10));

        await using var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(provider).AddAgent(JsonSchemaAgent()),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconStructuredResponseOptions>(options =>
                {
                    options.Enabled = true;
                    options.MaxRepairAttempts = 2;
                });
                services.Configure<TraconOptions>(o => o.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5));
            });

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        // Same pre-flight check as the token-budget test above, this time
        // tripped by AgentGraph.MaxDuration instead of MaxTotalTokens: the
        // deadline check runs BEFORE the model is called, so the repair turn
        // is blocked before it ever reaches the provider.
        run.Error!.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        provider.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_repair_turn_also_goes_through_the_fallback_chain()
    {
        var primary = new AlwaysUnavailableModelProvider("primary");
        var fallback = new FakeModelProvider("fallback")
            .RespondsWith("not valid json")
            .RespondsWith("{\"answer\":42}");

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(primary)
                .AddModelProvider(fallback)
                .AddAgent(JsonSchemaAgent(primaryProvider: "primary", fallbackProvider: "fallback")),
            configureServices: services => services.Configure<TraconStructuredResponseOptions>(options =>
            {
                options.Enabled = true;
                options.MaxRepairAttempts = 1;
            }));

        using var response = await PostBufferedAsync(host);

        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        fallback.Requests.Count.ShouldBe(2);

        var events = await SseFramesAsync(host, run.Id);

        // The primary failed over on BOTH model calls - the repair turn did
        // not take a shortcut around the fallback chain.
        events.Count(static frame => frame.Data.Contains("\"type\":\"ModelFallbackUsed\"", StringComparison.Ordinal))
            .ShouldBe(2);
    }

    [Fact]
    public async Task A_run_with_a_durable_session_gets_no_repair_budget_and_fails_closed()
    {
        var provider = new FakeModelProvider("structured")
            .RespondsWith("not valid json")
            .RespondsWith("{\"answer\":42}");

        await using var host = await StartAsync(provider, maxRepairAttempts: 1);

        using var response = await PostBufferedAsync(
            host, new AgentRunRequest { Message = "hello", SessionId = "repair-session" });

        // 🚨 Repair is CONFIGURED here, and deliberately not used. The framework
        // persists a turn's exchange as soon as that ONE model call completes,
        // before this decorator sees the result, so a repaired run would return
        // an answer its own session never recorded - the next turn would then
        // read a response the caller never received. The run fails instead.
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        // Exactly ONE model call: the repair turn never opened.
        provider.Requests.Count.ShouldBe(1);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);

        var events = await EventTypesAsync(host, run.Id);

        events.ShouldContain("StructuredResponseRejected");
        events.ShouldNotContain("StructuredResponseRepairAttempted");

        using var sessionResponse = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/repair-session", UriKind.Relative), TestContext.Current.CancellationToken);

        // Stronger than "the history matches the run": a session is persisted
        // only when its run COMPLETES SUCCESSFULLY, so a first turn that fails
        // this way leaves no durable history at all - the rejected draft never
        // becomes conversation the next turn could read. (A later turn in an
        // ALREADY saved session likewise leaves that session at its previous
        // turn's state.) Before the guard, this same run succeeded and saved
        // the rejected draft as the assistant's answer.
        sessionResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancellation_during_a_repair_turn_cancels_the_run_not_a_validation_failure()
    {
        var provider = new BlockingOnSecondCallProvider("not valid json");

        await using var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(provider).AddAgent(JsonSchemaAgent()),
            configureServices: services =>
            {
                services.Configure<TraconStructuredResponseOptions>(options =>
                {
                    options.Enabled = true;
                    options.MaxRepairAttempts = 1;
                });
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await provider.SecondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        provider.Release.TrySetResult();

        var run = await WaitForTerminalStatusAsync(host, runId);

        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error?.Class.ShouldNotBe(RunErrorClass.StructuredResponseInvalid);
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider, int? maxRepairAttempts, Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(provider).AddAgent(JsonSchemaAgent()),
            configureServices: services =>
            {
                services.Configure<TraconStructuredResponseOptions>(options =>
                {
                    options.Enabled = true;

                    if (maxRepairAttempts is { } max)
                    {
                        options.MaxRepairAttempts = max;
                    }
                });
                configureServices?.Invoke(services);
            });

    private static AgentDefinition JsonSchemaAgent(string primaryProvider = "structured", string? fallbackProvider = null) => new()
    {
        Name = AgentName,
        DisplayName = "Structured Repair Agent",
        Description = "An agent asking for a JSON Schema response, used by structured response repair tests.",
        Instructions = "Reply briefly.",
        Model = new ModelBinding
        {
            Provider = primaryProvider,
            Model = $"{primaryProvider}-1",
            ResponseFormat = new AgentResponseFormat
            {
                Kind = AgentResponseFormatKind.JsonSchema,
                Schema = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
                SchemaName = "answer",
            },
            Fallbacks = fallbackProvider is null
                ? []
                : [new ModelFallback { Provider = fallbackProvider, Model = $"{fallbackProvider}-1" }],
        },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static async Task<RunRecord> SingleRunAsync(TraconTestHost host)
    {
        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        return runs.ShouldHaveSingleItem();
    }

    private static async Task<string> EventTypesAsync(TraconTestHost host, Guid runId)
        => await host.Client.GetStringAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>
    /// Reads a completed run's event stream as SSE frames (the endpoint's real
    /// wire format - see <c>RunEndpoints.MapGet("/api/runs/{runId:guid}/events")</c>).
    /// </summary>
    private static async Task<List<SseFrame>> SseFramesAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative),
            TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);

        return await SseReader.ReadAllAsync(stream);
    }

    /// <summary>Polls a run until it reaches a terminal status.</summary>
    private static async Task<RunRecord> WaitForTerminalStatusAsync(TraconTestHost host, Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var run = await host.Client.GetFromJsonAsync<RunRecord>(
                new Uri($"/tracon/api/runs/{runId}", UriKind.Relative), TestContext.Current.CancellationToken);

            if (run is { Status: RunStatus.Completed or RunStatus.Failed or RunStatus.Canceled })
            {
                return run;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"Run {runId} did not reach a terminal status within 30 seconds.");
    }

    /// <summary>
    /// Enters the non-streaming branch. A request carrying the
    /// <c>Idempotency-Key</c> header runs with a single JSON response instead
    /// of SSE (Phase 43) - repair only ever runs on this branch (docs/134 §134.4).
    /// </summary>
    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, AgentRunRequest? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(body ?? new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A model provider whose every call advances a <see cref="ManualTimeProvider"/>
    /// by <paramref name="advancePerCall"/> before answering with invalid JSON -
    /// used to trip <c>AgentGraph.MaxDuration</c> deterministically without a real delay.
    /// </summary>
    private sealed class ClockAdvancingModelProvider(ManualTimeProvider clock, TimeSpan advancePerCall) : IModelProvider
    {
        public string Name => "structured";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "structured-1", SupportsStructuredOutput = true }];

        public int Calls { get; private set; }

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

        private void OnCall()
        {
            Calls++;
            clock.Advance(advancePerCall);
        }

        private sealed class Client(ClockAdvancingModelProvider owner) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                owner.OnCall();

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "not valid json")));
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException("This provider is used by the non-streaming path only.");

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resource to release.
            }
        }
    }

    /// <summary>
    /// A model provider that always throws <see cref="TraconProviderUnavailableException"/> —
    /// the ONE exception <c>FallbackRetryClassifier</c>'s closed set always
    /// treats as fallback-worthy, unlike a generic SDK-shaped failure (see
    /// <see cref="ThrowingModelProvider"/>, used elsewhere for the opposite case).
    /// </summary>
    private sealed class AlwaysUnavailableModelProvider(string name) : IModelProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = $"{name}-1", SupportsStructuredOutput = true }];

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(Name);

        private sealed class Client(string providerName) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new TraconProviderUnavailableException("simulated outage") { ProviderName = providerName };

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new TraconProviderUnavailableException("simulated outage") { ProviderName = providerName };

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resource to release.
            }
        }
    }

    /// <summary>
    /// A model provider whose first call answers immediately (with
    /// <paramref name="firstReply"/>) and whose SECOND call blocks until
    /// <see cref="Release"/> is completed, observing the caller's
    /// cancellation token - used to cancel a run while its repair turn is in flight.
    /// </summary>
    private sealed class BlockingOnSecondCallProvider(string firstReply) : IModelProvider
    {
        private int _calls;

        public string Name => "structured";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "structured-1", SupportsStructuredOutput = true }];

        public TaskCompletionSource SecondCallStarted { get; } = new();

        public TaskCompletionSource Release { get; } = new();

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this, firstReply);

        private sealed class Client(BlockingOnSecondCallProvider owner, string firstReply) : IChatClient
        {
            public async Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref owner._calls) == 1)
                {
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, firstReply));
                }

                owner.SecondCallStarted.TrySetResult();

                await owner.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"answer\":42}"));
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException("This provider is used by the non-streaming (queued) path only.");

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resource to release.
            }
        }
    }
}
