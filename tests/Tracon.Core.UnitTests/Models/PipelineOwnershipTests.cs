using System.Runtime.CompilerServices;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Proves that the tool-call loop belongs to <see cref="ModelProviderRegistry"/>
/// alone (K-320), by measuring the one thing that actually changes when a
/// provider builds its own.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A provider that wraps its client in <c>UseFunctionInvocation()</c> before
/// returning it produces the SAME reply text and the SAME tool-call count as a
/// correct one. Neither reveals the mistake. What changes is where the loop's
/// turns run: the registry places the content guard directly above the client
/// the provider returns, so a tool result re-entering the model is inspected as
/// <see cref="ContentGuardDirection.Input"/>. With an inner loop, that turn
/// runs beneath the guard and the tool result is only ever seen leaving —
/// which is precisely the path prompt injection takes.
/// </para>
/// <para>
/// The assertions deliberately avoid naming any pipeline implementation type.
/// Counting <c>FunctionInvokingChatClient</c> instances would bind this test to
/// the shape of the pipeline; what the contract actually promises is the
/// inspection, so that is what is measured.
/// </para>
/// </remarks>
public sealed class PipelineOwnershipTests
{
    private const string ToolName = "pipeline_probe_tool";
    private const string ToolResult = "probe-tool-result";
    private const string UserMessage = "call the tool";

    [Fact]
    public async Task A_raw_provider_lets_the_guard_inspect_the_tool_result_entering_the_model()
    {
        var run = await RunWithToolAsync(providerBuildsItsOwnLoop: false);

        run.InspectedEnteringTheModel(ToolResult).ShouldBeTrue(
            "the registry places the content guard inside the tool-call loop, so the turn "
            + "carrying a tool result back to the model must be inspected as Input");
    }

    [Fact]
    public async Task A_provider_that_builds_its_own_tool_call_loop_hides_the_tool_result_from_the_guard()
    {
        var raw = await RunWithToolAsync(providerBuildsItsOwnLoop: false);
        var nested = await RunWithToolAsync(providerBuildsItsOwnLoop: true);

        // The damage is invisible everywhere else: same answer, same tool call.
        nested.ResponseText.ShouldBe(raw.ResponseText);
        nested.ToolCalls.ShouldBe(raw.ToolCalls);

        nested.InspectedEnteringTheModel(ToolResult).ShouldBeFalse(
            "an inner tool-call loop feeds the tool result to the model beneath the guard");
        nested.Guard.CallCount.ShouldBeLessThan(raw.Guard.CallCount);
    }

    [Fact]
    public async Task The_registry_builds_the_tool_call_loop_even_though_the_provider_returns_a_raw_client()
    {
        var run = await RunWithToolAsync(providerBuildsItsOwnLoop: false);

        // A raw client cannot resolve a tool call by itself; that the tool ran
        // at all is what proves the registry supplied the loop.
        run.ToolCalls.ShouldBe(1);
        run.RawCalls.ShouldBe(2, "one turn to request the tool, one to answer with its result");
    }

    private static async Task<RunOutcome> RunWithToolAsync(bool providerBuildsItsOwnLoop)
    {
        var raw = new ScriptedChatClient();
        var guard = new StubContentGuard(static _ => ContentGuardResult.Allow);
        var toolCalls = 0;

        var tool = AIFunctionFactory.Create(
            () =>
            {
                Interlocked.Increment(ref toolCalls);
                return ToolResult;
            },
            ToolName,
            "Returns a fixed probe value.");

        var registry = TestData.Providers(
            TestData.ContentGuards(guards: guard),
            new ScriptedModelProvider(raw, providerBuildsItsOwnLoop));

        var compiler = new AgentDefinitionCompiler(registry, TestData.Registry(tool));
        var definition = TestData.Definition() with
        {
            Model = TestData.Binding(provider: ScriptedModelProvider.ProviderName, model: ScriptedModelProvider.ModelName),
            ToolNames = [ToolName],
        };

        var agent = await compiler.CompileAsync(definition, CancellationToken.None);
        var response = await agent.RunAsync(UserMessage);

        return new RunOutcome(guard, response.Text, toolCalls, raw.Calls);
    }

    private sealed record RunOutcome(StubContentGuard Guard, string ResponseText, int ToolCalls, int RawCalls)
    {
        /// <summary>
        /// Whether the guard saw <paramref name="text"/> on its way INTO the
        /// model, rather than only on its way out.
        /// </summary>
        public bool InspectedEnteringTheModel(string text)
            => Guard.SeenDirections
                .Zip(Guard.SeenText)
                .Any(seen => seen.First == ContentGuardDirection.Input
                    && seen.Second.Contains(text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Asks for one tool call on the first turn that offers tools, then
    /// answers. Counts the calls that actually reach it.
    /// </summary>
    private sealed class ScriptedChatClient : IChatClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);

            return Task.FromResult(call == 1 && options?.Tools is { Count: > 0 }
                ? new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", ToolName, new Dictionary<string, object?>(StringComparer.Ordinal))]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release; Tracon never calls this anyway.
        }
    }

    /// <summary>
    /// A provider that either honors the contract (returns the raw client) or
    /// breaks it the way a third-party implementation most plausibly would.
    /// </summary>
    private sealed class ScriptedModelProvider(IChatClient inner, bool buildsItsOwnLoop) : IModelProvider
    {
        public const string ProviderName = "scripted";
        public const string ModelName = "scripted-model";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        public IChatClient CreateChatClient(ModelBinding binding)
            => buildsItsOwnLoop
                ? inner.AsBuilder().UseFunctionInvocation().Build()
                : inner;
    }
}
