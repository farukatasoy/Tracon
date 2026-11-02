using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// Cancels a run at every await point it has, one run per point, and checks the same things each time: the run
/// reaches a terminal state, the caller sees a cancellation or a result and nothing else, and nothing is left
/// registered, open or running.
/// </summary>
/// <remarks>
/// <para>
/// A first run with no cancellation counts the awaited calls the run makes on its store, its trace store, its
/// event sink, its model and its tool (the plan numbers them in the order they happen). Then run <c>n</c> cancels
/// the run exactly when call <c>n</c> arrives, for <c>n</c> from 1 to that count. Two ways to cancel are swept (the
/// caller's token, and the operator's cancel through the registry), two kinds of store (one that ignores its token
/// and one that throws when its token is already canceled, as a SQL store does), and both entry points.
/// </para>
/// <para>
/// The oracle is the invariant, not a recorded sequence: a run row that is not <c>Running</c> or <c>Queued</c>, an
/// empty registry, every activity the run started stopped, and a bounded wait. Which call was number <c>n</c> may
/// shift between runs when two writes race; the sweep still visits every position.
/// </para>
/// </remarks>
public sealed class RunCancellationSweepTests
{
    private static readonly TimeSpan RunBound = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task A_run_cancelled_at_any_await_point_ends_in_a_terminal_state_and_leaks_nothing(
        bool streaming,
        bool storeHonorsToken,
        bool operatorCancel)
    {
        var awaitPoints = (await RunAsync(streaming, storeHonorsToken, operatorCancel, cancelAt: null)).AwaitPoints;

        awaitPoints.ShouldBeGreaterThan(15, "the run must cross a store, a model, a tool and a sink");

        var problems = new List<string>();

        for (var cancelAt = 1; cancelAt <= awaitPoints; cancelAt++)
        {
            var outcome = await RunAsync(streaming, storeHonorsToken, operatorCancel, cancelAt);

            problems.AddRange(outcome.Problems.Select(problem => $"cancel at call {cancelAt} ({outcome.CancelledAt ?? "never reached"}): {problem}"));
        }

        UnobservedTaskGuard.ForceCollection();

        problems.ShouldBeEmpty();
    }

    private static async Task<Outcome> RunAsync(bool streaming, bool storeHonorsToken, bool operatorCancel, int? cancelAt)
    {
        var plan = new FaultPlan { HonorCancellation = storeHonorsToken };
        using var cts = new CancellationTokenSource();
        var registry = new RunCancellationRegistry();
        var awaited = 0;
        string? cancelledAt = null;

        plan.OnCall = call =>
        {
            if (!call.Method.EndsWith("Async", StringComparison.Ordinal))
            {
                return;
            }

            if (Interlocked.Increment(ref awaited) != cancelAt)
            {
                return;
            }

            cancelledAt = call.Method;

            if (operatorCancel)
            {
                foreach (var runId in registry.ActiveRunIds)
                {
                    registry.TryCancel(runId, "default");
                }
            }
            else
            {
                cts.Cancel();
            }
        };

        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var traceStore = new InMemoryTraceStore(FixedTenantContext.Default);
        var tracingOptions = new TraconOptions();
        tracingOptions.Observability.SuccessSampleRatio = 1;

        using var collector = new RunTraceCollector(
            FaultProxy<ITraceStore>.Create(traceStore, plan),
            Options.Create(tracingOptions),
            NullLogger<RunTraceCollector>.Instance);

        using var activities = new ActivityCounter();

        var tool = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await plan.EnterAsync("ToolInvokeAsync", cancellationToken).ConfigureAwait(false);
                await Task.Yield();

                return "result";
            },
            "lookup");

        var model = FaultProxy<IChatClient>.Create(new ScriptedChatClient(), plan);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider(model)), TestData.Registry(tool));

        var agent = new RunRecordingAgent(
            compiler.Compile(TestData.Definition(toolNames: ["lookup"])),
            FaultProxy<IRunStore>.Create(store, plan),
            FixedTenantContext.Default,
            new TraconRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            traceCollector: collector,
            cancellationRegistry: registry,
            sinks: [FaultProxy<IRunEventSink>.Create(new NullSink(), plan)]);

        Exception? failure = null;
        var finished = Stopwatch.StartNew();

        try
        {
            await Task.Run(() => DriveAsync(agent, streaming, cts.Token), CancellationToken.None).WaitAsync(RunBound, CancellationToken.None);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var problems = new List<string>();

        if (failure is TimeoutException)
        {
            problems.Add($"the run did not finish within {RunBound.TotalSeconds:0} s (I-03/I-08)");
        }
        else if (failure is not null && failure is not OperationCanceledException)
        {
            problems.Add($"the caller got {failure.GetType().Name}: {failure.Message}");
        }

        var runs = await store.QueryRunsAsync(new RunQuery());

        foreach (var run in runs.Where(static run => run.Status is RunStatus.Running or RunStatus.Queued))
        {
            problems.Add($"run {run.Id} is still {run.Status} (I-03)");
        }

        if (registry.ActiveCount != 0)
        {
            problems.Add($"{registry.ActiveCount} run(s) are still registered as active");
        }

        if (activities.Open != 0)
        {
            problems.Add($"{activities.Open} activity(ies) started and never stopped");
        }

        return new Outcome(awaited, cancelledAt, problems);
    }

    private static async Task DriveAsync(RunRecordingAgent agent, bool streaming, CancellationToken cancellationToken)
    {
        if (streaming)
        {
            await foreach (var _ in agent.RunStreamingAsync("hello", cancellationToken: cancellationToken))
            {
                // Frames are drained; the sweep looks at the terminal state.
            }

            return;
        }

        await agent.RunAsync("hello", cancellationToken: cancellationToken);
    }

    private sealed record Outcome(int AwaitPoints, string? CancelledAt, List<string> Problems);

    /// <summary>
    /// Counts the activities of the Tracon source that started and did not stop, among those of one trace: the
    /// test opens a root activity of its own and the run's spans become its children. Other tests of the process
    /// start activities on the same source at the same time; a count of the whole source would see theirs.
    /// </summary>
    private sealed class ActivityCounter : IDisposable
    {
        private const string SourceName = "Tracon.Tests.CancellationSweep";

        private static readonly ActivitySource Source = new(SourceName);

        private readonly ActivityListener _listener;
        private readonly Activity _root;
        private int _open;

        public ActivityCounter()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = static source => string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal)
                    || string.Equals(source.Name, SourceName, StringComparison.Ordinal),
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = OnStarted,
                ActivityStopped = OnStopped,
            };

            ActivitySource.AddActivityListener(_listener);

            _root = Source.StartActivity("sweep", ActivityKind.Internal, parentContext: default)
                ?? throw new InvalidOperationException("The test activity source has no listener.");
        }

        public int Open => Volatile.Read(ref _open);

        public void Dispose()
        {
            _root.Dispose();
            _listener.Dispose();
        }

        private bool IsRunSpan(Activity activity)
            => _root is not null && !ReferenceEquals(activity, _root) && activity.TraceId == _root.TraceId;

        private void OnStarted(Activity activity)
        {
            if (IsRunSpan(activity))
            {
                Interlocked.Increment(ref _open);
            }
        }

        private void OnStopped(Activity activity)
        {
            if (IsRunSpan(activity))
            {
                Interlocked.Decrement(ref _open);
            }
        }
    }

    /// <summary>A model that asks for the tool on its first turn and answers on its second, streaming or not.</summary>
    private sealed class ScriptedChatClient : IChatClient
    {
        private int _turn;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ContentOf(Interlocked.Increment(ref _turn))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            yield return new ChatResponseUpdate(ChatRole.Assistant, [.. ContentOf(Interlocked.Increment(ref _turn))]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }

        private static List<AIContent> ContentOf(int turn)
            => turn == 1
                ? [new FunctionCallContent("call-1", "lookup", new Dictionary<string, object?>(StringComparer.Ordinal))]
                : [new TextContent("done")];
    }

    private sealed class NullSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
