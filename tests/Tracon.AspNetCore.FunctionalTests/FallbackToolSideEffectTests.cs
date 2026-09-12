using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool call completed on the primary provider must not run a second time
/// when a fallback link asks the same question again — crossing the boundary
/// unit tests cannot: a real HTTP request, a real DI-built pipeline, and the
/// real <c>FunctionInvokingChatClient</c> loop driving a real registered tool.
/// </summary>
/// <remarks>
/// The model side is scripted (<see cref="StepModelProvider"/>, a small local
/// fake — <see cref="Infrastructure.ThrowingModelProvider"/> only ever throws
/// and cannot also script a preceding tool call); the tool side is a real
/// <c>[TraconTool]</c> method that counts how many times its body ran.
/// </remarks>
public sealed class FallbackToolSideEffectTests
{
    private const string AgentName = "fallback-tool-agent";
    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task A_side_effecting_tool_runs_once_even_though_the_fallback_asks_for_it_again()
    {
        ChargeProbeTool.Reset();

        var primary = new StepModelProvider(
            "primary-provider",
            Step.CallTool("charge_customer", "orderId", "ORD-1"),
            Step.Throw(new TraconProviderUnavailableException("simulated outage") { ProviderName = "primary-provider" }));

        var fallback = new StepModelProvider(
            "fallback-provider",
            Step.CallTool("charge_customer", "orderId", "ORD-1"),
            Step.Text("charged ORD-1"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(primary)
            .AddModelProvider(fallback)
            .AddToolsFrom(typeof(ChargeProbeTool))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Charge the customer's order when asked.",
                Model = new ModelBinding
                {
                    Provider = "primary-provider",
                    Model = "primary-model",
                    Fallbacks = [new ModelFallback { Provider = "fallback-provider", Model = "fallback-model" }],
                },
                ToolNames = ["charge_customer"],
            }));

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = "please charge ORD-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runId = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();

        // The defect this phase fixes: the tool body must run exactly ONCE,
        // even though both the primary and the fallback asked for it.
        ChargeProbeTool.Calls.ShouldBe(1);

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services
            .GetRequiredService<IRunStore>()
            .ReadEventsAsync(runId, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(runEvent);
        }

        // Confirms the fallback link genuinely engaged — otherwise the
        // "ran once" assertion above would be trivially true for the wrong
        // reason (the primary never having failed at all).
        events.ShouldContain(e => e.Type == RunEventType.ModelFallbackUsed);
    }

    [Fact]
    public async Task A_primary_that_never_fails_over_still_runs_a_repeated_identical_call_twice()
    {
        // Regression (found by independent audit): the primary link is
        // wrapped with the ledger too — necessarily, so a LATER link can
        // reuse its results — but when the primary itself calls the same
        // tool with the same arguments twice and simply succeeds (no
        // fallback ever triggers), the second call must run for real, not
        // be silently answered from what the first one just recorded.
        // Fallbacks is non-empty (so FallbackChatClient — and its ledger —
        // is actually in the pipeline); the fallback provider itself is
        // never called.
        ChargeProbeTool.Reset();

        var primary = new StepModelProvider(
            "primary-provider-4",
            Step.CallTool("charge_customer", "orderId", "ORD-1"),
            Step.CallTool("charge_customer", "orderId", "ORD-1"),
            Step.Text("charged ORD-1 twice"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(primary)
            .AddToolsFrom(typeof(ChargeProbeTool))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Charge the customer's order when asked.",
                Model = new ModelBinding
                {
                    Provider = "primary-provider-4",
                    Model = "primary-model",
                    Fallbacks = [new ModelFallback { Provider = "unused-fallback-provider", Model = "unused-model" }],
                },
                ToolNames = ["charge_customer"],
            }));

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = "please charge ORD-1 twice" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ChargeProbeTool.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Falling_back_once_does_not_leave_the_compiled_agents_options_permanently_wrapped()
    {
        ChargeProbeTool.Reset();

        var primary = new StepModelProvider(
            "primary-provider-2",
            Step.CallTool("charge_customer", "orderId", "ORD-1"),
            Step.Throw(new TraconProviderUnavailableException("simulated outage") { ProviderName = "primary-provider-2" }),

            // Second run: the primary succeeds outright — no fallback needed.
            // If the FIRST run had left the compiled agent's ChatOptions.Tools
            // permanently wrapped, this call would see a stale ledger from the
            // FIRST run's fallback client instance, or the compiled agent
            // itself would misbehave.
            Step.CallTool("charge_customer", "orderId", "ORD-2"),
            Step.Text("charged ORD-2"));

        var fallback = new StepModelProvider(
            "fallback-provider-2",
            Step.CallTool("charge_customer", "orderId", "ORD-1"),
            Step.Text("charged ORD-1"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(primary)
            .AddModelProvider(fallback)
            .AddToolsFrom(typeof(ChargeProbeTool))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Charge the customer's order when asked.",
                Model = new ModelBinding
                {
                    Provider = "primary-provider-2",
                    Model = "primary-model",
                    Fallbacks = [new ModelFallback { Provider = "fallback-provider-2", Model = "fallback-model" }],
                },
                ToolNames = ["charge_customer"],
            }));

        using var first = await PostBufferedAsync(host, new AgentRunRequest { Message = "please charge ORD-1" });
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        ChargeProbeTool.Calls.ShouldBe(1);

        using var second = await PostBufferedAsync(host, new AgentRunRequest { Message = "please charge ORD-2" });
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        // ORD-2 is a call the ledger never saw: it must run for real, on the
        // SAME compiled (cached) agent the first, fallback-triggering run used.
        ChargeProbeTool.Calls.ShouldBe(2);
        ChargeProbeTool.LastOrderId.ShouldBe("ORD-2");
    }

    [Fact]
    public async Task Two_concurrent_tool_calls_both_answered_correctly_when_the_fallback_repeats_them()
    {
        ChargeProbeTool.Reset();

        var primary = new StepModelProvider(
            "primary-provider-3",
            Step.CallTools(
                ("charge_customer", "orderId", (object?)"ORD-1"),
                ("charge_customer", "orderId", (object?)"ORD-2")),
            Step.Throw(new TraconProviderUnavailableException("simulated outage") { ProviderName = "primary-provider-3" }));

        var fallback = new StepModelProvider(
            "fallback-provider-3",
            Step.CallTools(
                ("charge_customer", "orderId", (object?)"ORD-1"),
                ("charge_customer", "orderId", (object?)"ORD-2")),
            Step.Text("charged both"));

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(primary)
            .AddModelProvider(fallback)
            .AddToolsFrom(typeof(ChargeProbeTool))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Charge both orders when asked.",
                Model = new ModelBinding
                {
                    Provider = "primary-provider-3",
                    Model = "primary-model",
                    AllowConcurrentToolCalls = true,
                    Fallbacks = [new ModelFallback { Provider = "fallback-provider-3", Model = "fallback-model" }],
                },
                ToolNames = ["charge_customer"],
            }));

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = "please charge ORD-1 and ORD-2" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Both concurrent calls settle into the ledger without a race
        // corrupting either entry; the fallback's repeat of both is answered
        // from there, so each body runs exactly once.
        ChargeProbeTool.Calls.ShouldBe(2);
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Builds the individual scripted steps <see cref="StepModelProvider"/> plays
/// back in order.
/// </summary>
file static class Step
{
    public static Func<ChatResponse> CallTool(string toolName, string argumentName, object? argumentValue)
        => () => new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent(Guid.NewGuid().ToString("N"), toolName, Arguments(argumentName, argumentValue))]));

    public static Func<ChatResponse> CallTools(params (string ToolName, string ArgumentName, object? ArgumentValue)[] calls)
        => () => new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [.. calls.Select(static call =>
                (AIContent)new FunctionCallContent(
                    Guid.NewGuid().ToString("N"), call.ToolName, Arguments(call.ArgumentName, call.ArgumentValue)))]));

    public static Func<ChatResponse> Text(string text) => () => new ChatResponse(new ChatMessage(ChatRole.Assistant, text));

    public static Func<ChatResponse> Throw(Exception exception) => () => throw exception;

    private static Dictionary<string, object?> Arguments(string name, object? value)
        => new(StringComparer.Ordinal) { [name] = value };
}

/// <summary>
/// A model provider driven by a fixed, ordered list of steps: each call pops
/// the next one; the last step repeats once the list is exhausted.
/// </summary>
/// <remarks>
/// Unlike <see cref="Tracon.Testing.FakeModelProvider"/>, a step can
/// THROW — needed to script "the primary calls a tool, then the outage
/// hits" as two separate, ordered calls to the SAME provider.
/// </remarks>
file sealed class StepModelProvider(string name, params Func<ChatResponse>[] steps) : IModelProvider
{
    private int _index = -1;

    public string Name { get; } = name;

    public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = name + "-model" }];

    public IChatClient CreateChatClient(ModelBinding binding) => new StepChatClient(this);

    private ChatResponse NextStep()
    {
        var index = Math.Min(Interlocked.Increment(ref _index), steps.Length - 1);
        return steps[index]();
    }

    private sealed class StepChatClient(StepModelProvider owner) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(owner.NextStep());

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test suite only exercises the non-streaming (Idempotency-Key) path.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resource to release.
        }
    }
}

/// <summary>A real registered tool that counts how many times its body ran.</summary>
file static class ChargeProbeTool
{
    private static int _calls;
    private static string? _lastOrderId;

    public static int Calls => Volatile.Read(ref _calls);

    public static string? LastOrderId => Volatile.Read(ref _lastOrderId);

    public static void Reset()
    {
        Volatile.Write(ref _calls, 0);
        Volatile.Write(ref _lastOrderId, null);
    }

    [TraconTool("charge_customer", "Charges the customer for an order. Not safe to repeat.")]
    public static string ChargeCustomer(string orderId)
    {
        Interlocked.Increment(ref _calls);
        Volatile.Write(ref _lastOrderId, orderId);

        return $"{orderId}: charged {Guid.NewGuid():N}";
    }
}
