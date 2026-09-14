using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Tracon.Capacity;

namespace Tracon.CapacityHost;

/// <summary>A deterministic model provider that makes no network call.</summary>
/// <remarks>
/// <para>
/// 🚨 The provider is the ONLY thing replaced. Everything above it stays real:
/// Microsoft Agent Framework's tool loop, Tracon's compiler, the recording
/// path and the store. Replacing more than the provider would turn the
/// measurement into a measurement of the fixture.
/// </para>
/// <para>
/// 🚨 It decides "is this the answering turn?" from the MESSAGES it was handed,
/// never from a global counter. A counter would tie two concurrent runs
/// together, and under load the fixture itself would become the thing that
/// fails. State is kept per run, keyed by the correlation value the request
/// carries.
/// </para>
/// </remarks>
public sealed class CapacityModelProvider : IModelProvider
{
    private readonly HostWorkload _workload;
    private readonly CapacityCounters _counters;
    private readonly CapacityExecutionLog _executions;
    private readonly string _processName;

    /// <summary>Creates the provider.</summary>
    /// <param name="workload">The synthetic workload to replay.</param>
    /// <param name="counters">Where the host's own counters live.</param>
    /// <param name="executions">Where this process records what it served.</param>
    /// <param name="processName">This process's label.</param>
    public CapacityModelProvider(
        HostWorkload workload,
        CapacityCounters counters,
        CapacityExecutionLog executions,
        string processName)
    {
        _workload = workload;
        _counters = counters;
        _executions = executions;
        _processName = processName;
    }

    /// <inheritdoc />
    public string Name => CapacityContract.ProviderName;

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new ModelDescriptor
        {
            Name = CapacityContract.ModelName,
            DisplayName = "Capacity fixture (deterministic, no network)",
            ContextWindowTokens = 128_000,
            MaxOutputTokens = 16_000,
            SupportsTools = true,
        },
    ];

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new CapacityChatClient(_workload, _counters, _executions, _processName);
    }

    private sealed class CapacityChatClient : IChatClient
    {
        private readonly HostWorkload _workload;
        private readonly CapacityCounters _counters;
        private readonly CapacityExecutionLog _executions;
        private readonly string _processName;

        public CapacityChatClient(
            HostWorkload workload,
            CapacityCounters counters,
            CapacityExecutionLog executions,
            string processName)
        {
            _workload = workload;
            _counters = counters;
            _executions = executions;
            _processName = processName;
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var turn = Begin(messages, streaming: false);
            await Task.Delay(turn.DelayMilliseconds, cancellationToken).ConfigureAwait(false);

            if (turn.CallTool)
            {
                var call = new ChatResponse(new ChatMessage(ChatRole.Assistant, [ToolCall(turn.Correlation)]))
                {
                    ModelId = CapacityContract.ModelName,
                };

                Finish(turn);
                return call;
            }

            var answer = CapacityPayload.Answer(
                turn.Correlation,
                _workload.OutputChunks,
                _workload.OutputChunkCharacters);

            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
            {
                ModelId = CapacityContract.ModelName,
                Usage = Usage(turn.Correlation, answer),
            };

            Finish(turn);
            return response;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var turn = Begin(messages, streaming: true);

            if (turn.CallTool)
            {
                await Task.Delay(turn.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [ToolCall(turn.Correlation)])
                {
                    ModelId = CapacityContract.ModelName,
                };

                Finish(turn);
                yield break;
            }

            var chunks = CapacityPayload.AnswerChunks(
                turn.Correlation,
                _workload.OutputChunks,
                _workload.OutputChunkCharacters);

            // The wait is spread across the deltas rather than taken up front:
            // a burst after one long pause would measure a buffered answer with
            // an SSE wrapper, not a streamed one.
            var perChunk = chunks.Count == 0 ? turn.DelayMilliseconds : turn.DelayMilliseconds / chunks.Count;

            foreach (var chunk in chunks)
            {
                if (perChunk > 0)
                {
                    await Task.Delay(perChunk, cancellationToken).ConfigureAwait(false);
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { ModelId = CapacityContract.ModelName };
            }

            Finish(turn);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The client owns no unmanaged resource; the state dictionary is
            // shared and is cleaned up as each run completes.
        }

        private static FunctionCallContent ToolCall(string correlation)
            => new(
                Guid.NewGuid().ToString("N"),
                CapacityPayload.ToolName,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["correlation"] = correlation,
                });

        private Turn Begin(IEnumerable<ChatMessage> messages, bool streaming)
        {
            var list = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
            var correlation = FindCorrelation(list);
            var toolAnswered = list.Any(static message => message.Contents.OfType<FunctionResultContent>().Any());

            // 🚨 Read from the messages, not from a counter: with two runs in
            // flight a counter would hand one run's second turn to the other.
            var callTool = _workload.ToolCall && !toolAnswered;
            var turns = _workload.ToolCall ? 2 : 1;
            var delay = _workload.ModelDelayMilliseconds / Math.Max(1, turns);

            var state = CapacityRunLedger.GetOrStart(correlation);
            state.Turns++;
            state.Streaming = streaming;
            _counters.ModelTurn();

            return new Turn(correlation, callTool, delay, state);
        }

        private void Finish(Turn turn)
        {
            turn.State.ModelMilliseconds += Stopwatch.GetElapsedTime(turn.Started).TotalMilliseconds;

            if (turn.CallTool)
            {
                return;
            }

            _counters.ModelCall();

            if (CapacityRunLedger.Finish(turn.Correlation) is { } state)
            {
                _executions.Append(new CapacityExecution
                {
                    Correlation = turn.Correlation,
                    Tenant = "",
                    Pid = Environment.ProcessId,
                    Process = _processName,
                    StartedTicks = state.StartedUtcTicks,
                    CompletedTicks = DateTime.UtcNow.Ticks,
                    Turns = state.Turns,
                    ModelMilliseconds = Math.Round(state.ModelMilliseconds, 3),
                    ToolCalls = state.ToolCalls,
                    Streaming = state.Streaming,
                });
            }
        }

        private static UsageDetails Usage(string correlation, string answer) => new()
        {
            InputTokenCount = correlation.Length,
            OutputTokenCount = answer.Length / 4,
            TotalTokenCount = correlation.Length + (answer.Length / 4),
        };

        private static string FindCorrelation(IReadOnlyList<ChatMessage> messages)
        {
            foreach (var message in messages)
            {
                var correlation = CapacityPayload.ExtractCorrelation(message.Text);

                if (correlation.Length > 0)
                {
                    return correlation;
                }
            }

            // A run with no correlation is a fixture failure, not a model
            // answer. It is given a value that can never match the driver's
            // expectation, so the mismatch shows up in the reconciliation
            // rather than silently passing.
            return "missing-correlation-" + Guid.NewGuid().ToString("N");
        }

        private sealed record Turn(string Correlation, bool CallTool, int DelayMilliseconds, CapacityRunState State)
        {
            public long Started { get; } = Stopwatch.GetTimestamp();
        }
    }
}
