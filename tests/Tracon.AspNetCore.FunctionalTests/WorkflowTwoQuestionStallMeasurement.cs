using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Measures whether the stream of the answer to one of two open questions ever fails to end: the
/// first answer is sent over HTTP, and the response stream must end with the workflow's output or
/// with the second question still open.
/// </summary>
/// <remarks>
/// <para>
/// The graph asks two questions at once. Answering one resumes the run from its checkpoint; the
/// runner then reads the status of the run and decides whether to open the event stream again, and
/// that decision is one flag (<c>readSinceResponse</c>) shared by every request the run has
/// answered. A probe of the underlying framework with a graph of this shape left the reopened
/// stream waiting on an empty channel in about 0.2 % of runs. Here the same graph runs through
/// Tracon's own path, where the answer's run row, the answer record and the checkpoint write all
/// happen between the answer and the status read, as in real use.
/// </para>
/// <para>
/// Each iteration starts the workflow, answers the first question and waits up to
/// <see cref="StallBound"/> for the response stream to end. A stream that does not end in that time
/// is a <em>stall</em>. A stall is counted and described, not thrown on the spot, so one batch
/// reports every stall it hit; the test fails at the end if there was one. A stream that ends
/// without the output and without the second question is counted separately, because it is the
/// opposite defect (an answer applied and lost).
/// </para>
/// <para>
/// The default (25 runs, 8 at a time) is small enough for the closing gate. A measurement raises it
/// and runs the compiled test executable:
/// </para>
/// <para>
/// A stall used to be a known defect of the framework's run loop (measured at about 1 run in 10 000 with 8 at a
/// time, none with 1 at a time): a stream reopened after another one had read the loop's halt signal waited for
/// ever. The pump now ends such a wait (<c>StalledStreamGuard</c>, proved with the real framework in
/// <c>StalledStreamGuardTests</c>), so any stall fails, the default run included.
/// </para>
/// <list type="bullet">
/// <item><c>TRACON_STALL_RUNS</c> - iterations (default 25).</item>
/// <item><c>TRACON_STALL_PARALLEL</c> - iterations in flight at once on the one host (default 8).</item>
/// </list>
/// <para>
/// <c>TRACON_STALL_RUNS=1000 TRACON_STALL_PARALLEL=8 artifacts/bin/Tracon.AspNetCore.FunctionalTests/release/Tracon.AspNetCore.FunctionalTests --filter-class '*WorkflowTwoQuestionStallMeasurement*'</c>.
/// An iteration makes no random choice, but a stall is a race: the same two numbers give the same
/// odds, not the same iteration. A stall report names the iteration, how many iterations were in
/// flight with it, the last frames the stream emitted and, while the run store still holds them,
/// the response run's row and events (the in-memory run store keeps only its latest 1 000 runs, so
/// in a long batch the row of an early iteration is gone when the bound passes).
/// </para>
/// </remarks>
public sealed class WorkflowTwoQuestionStallMeasurement
{
    /// <summary>The workflow name.</summary>
    private const string Name = "two-questions";

    /// <summary>The port of the question the iteration answers.</summary>
    private const string FirstPort = "first-port";

    /// <summary>The port of the question that stays open.</summary>
    private const string SecondPort = "second-port";

    private const string RunsVariable = "TRACON_STALL_RUNS";

    private const string ParallelVariable = "TRACON_STALL_PARALLEL";

    private const int DefaultRuns = 25;

    private const int DefaultParallel = 8;

    /// <summary>How long a response stream may take to end before it counts as a stall.</summary>
    private static readonly TimeSpan StallBound = TimeSpan.FromSeconds(30);

    /// <summary>How long the start of an iteration may take before the iteration counts as failed.</summary>
    private static readonly TimeSpan StartBound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task The_stream_of_the_first_answer_to_a_two_question_workflow_always_ends()
    {
        var runs = Setting(RunsVariable, DefaultRuns);
        var parallel = Setting(ParallelVariable, DefaultParallel);
        var token = TestContext.Current.CancellationToken;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.UseWorkflows();
                builder.AddWorkflow(
                    Name,
                    static _ => BuildTwoQuestions(),
                    static options => options.ResponseMode = WorkflowResponseMode.SingleAnswer);
            });

        var failures = new ConcurrentBag<Failure>();
        var ended = new EndTally();
        var inFlight = 0;
        var elapsed = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, runs),
            new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = token },
            async (iteration, iterationToken) =>
            {
                var concurrent = Interlocked.Increment(ref inFlight);

                try
                {
                    if (await AnswerOnceAsync(host, iteration, concurrent, ended, iterationToken) is { } failure)
                    {
                        failures.Add(failure);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            });

        elapsed.Stop();

        var ordered = failures.OrderBy(static failure => failure.Iteration).ToList();
        var stalls = ordered.Count(static failure => failure.Verdict == Verdict.Stalled);
        var lost = ordered.Count(static failure => failure.Verdict == Verdict.EndedWithoutResult);
        var errors = ordered.Count(static failure => failure.Verdict == Verdict.Failed);

        var wall = elapsed.Elapsed.TotalSeconds;

        Console.WriteLine(FormattableString.Invariant(
            $"TWO-QUESTION-STALL runs={runs} parallel={parallel} stalls={stalls} ended-without-result={lost} errors={errors} output={ended.Outputs} second-question-open={ended.Awaiting} wall={wall:F1} s ({runs / wall:F1} runs/s)"));

        var summary = FormattableString.Invariant(
            $"{stalls} stalled, {lost} ended without the output or the second question, {errors} failed, of {runs} runs ({RunsVariable}={runs}, {ParallelVariable}={parallel}, {StallBound.TotalSeconds:0} s bound, {wall:F1} s wall).");

        ordered.Count.ShouldBe(0, summary + Environment.NewLine + string.Join(Environment.NewLine, ordered.Select(static failure => failure.ToString())));
    }

    private static async Task<Failure?> AnswerOnceAsync(TraconTestHost host, int iteration, int concurrent, EndTally ended, CancellationToken cancellationToken)
    {
        Guid? sourceId = null;
        Guid? responseId = null;
        var requestId = string.Empty;
        var frames = new List<SseFrame>();

        try
        {
            (sourceId, requestId) = await StartAsync(host, cancellationToken);

            var sent = Stopwatch.StartNew();
            using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var drain = DrainAsync(host, sourceId.Value, requestId, frames, abandon.Token);

            try
            {
                await drain.WaitAsync(StallBound, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Described BEFORE the stream is abandoned: closing it changes what the run row and the stream hold.
                responseId = ResponseRunOf(frames);

                var held = await DescribeAsync(host, sourceId.Value, responseId, requestId, frames);
                var stalled = new Failure(
                    iteration,
                    Verdict.Stalled,
                    FormattableString.Invariant($"iteration {iteration}, {concurrent} in flight when it started, no end after {sent.Elapsed.TotalSeconds:F1} s") + Environment.NewLine + held);

                await abandon.CancelAsync();
                await Settle(drain);

                return stalled;
            }

            responseId = ResponseRunOf(frames);

            if (Classify(frames) is not { } verdict)
            {
                ended.Count(frames);

                return null;
            }

            return new Failure(
                iteration,
                verdict,
                FormattableString.Invariant($"iteration {iteration}, {concurrent} in flight when it started, the stream ended after {sent.Elapsed.TotalSeconds:F1} s")
                    + Environment.NewLine
                    + await DescribeAsync(host, sourceId.Value, responseId, requestId, frames));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new Failure(
                iteration,
                Verdict.Failed,
                FormattableString.Invariant(
                    $"iteration {iteration}, {concurrent} in flight when it started, source run {sourceId}, response run {responseId}: {exception.GetType().Name}: {exception.Message}"));
        }
    }

    /// <summary>Starts the workflow and returns the run that waits and the request id of the first question.</summary>
    private static async Task<(Guid SourceId, string RequestId)> StartAsync(TraconTestHost host, CancellationToken cancellationToken)
    {
        // The start is bounded too: a start that never ends would hang the whole batch instead of failing one iteration.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(StartBound);

        try
        {
            using var started = await host.Client.PostAsJsonAsync(
                $"/tracon/api/workflows/{Name}/run",
                new WorkflowRunHttpRequest { Message = "the draft" },
                bounded.Token);

            started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(bounded.Token));

            var startFrames = new List<SseFrame>();

            await foreach (var frame in SseReader.ReadAsync(await started.Content.ReadAsStreamAsync(bounded.Token), bounded.Token))
            {
                startFrames.Add(frame);
            }

            var sourceId = RunIdOf(startFrames);

            using var listed = await host.Client.GetAsync($"/tracon/api/workflows/runs/{sourceId}/requests", bounded.Token);
            var requests = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ToList();

            requests.Count.ShouldBe(2, $"the start of run {sourceId} should leave both questions open");

            return (
                sourceId,
                requests.Single(static item => string.Equals(item.GetProperty("portId").GetString(), FirstPort, StringComparison.Ordinal)).GetProperty("requestId").GetString()!);
        }
        catch (OperationCanceledException exception) when (bounded.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The start did not end within {StartBound.TotalSeconds:0} s.", exception);
        }
    }

    /// <summary>Sends the answer and reads its stream to the end, keeping every frame as it arrives.</summary>
    private static async Task DrainAsync(
        TraconTestHost host,
        Guid sourceId,
        string requestId,
        List<SseFrame> frames,
        CancellationToken cancellationToken)
    {
        using var answered = await RespondAsync(host, sourceId, new { requestId, approved = true }, tenant: null, cancellationToken);

        answered.StatusCode.ShouldBe(HttpStatusCode.OK, await ProblemTextAsync(answered));

        await foreach (var frame in SseReader.ReadAsync(await answered.Content.ReadAsStreamAsync(cancellationToken), cancellationToken))
        {
            lock (frames)
            {
                frames.Add(frame);
            }
        }
    }

    /// <summary>The way a finished stream is wrong, or <see langword="null"/> when it ended as it should.</summary>
    private static Verdict? Classify(List<SseFrame> frames)
    {
        lock (frames)
        {
            if (frames.Count == 0 || !string.Equals(frames[^1].Event, "done", StringComparison.Ordinal))
            {
                return Verdict.Failed;
            }

            return frames.Any(static frame => string.Equals(frame.Event, "event", StringComparison.Ordinal)
                    && (frame.Data.Contains("\"WorkflowOutput\"", StringComparison.Ordinal)
                        || frame.Data.Contains("\"RunAwaitingInput\"", StringComparison.Ordinal)))
                ? null
                : Verdict.EndedWithoutResult;
        }
    }

    private static Guid? ResponseRunOf(List<SseFrame> frames)
    {
        lock (frames)
        {
            return frames.Count > 0 && string.Equals(frames[0].Event, "run", StringComparison.Ordinal)
                ? JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid()
                : null;
        }
    }

    /// <summary>What the stream, the run row, the stored events and the log held for a stream that did not end as it should.</summary>
    private static async Task<string> DescribeAsync(TraconTestHost host, Guid sourceId, Guid? responseId, string requestId, List<SseFrame> frames)
    {
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture, $"  source run {sourceId}, answered request {requestId}, response run {responseId?.ToString() ?? "(none announced)"}");

        lock (frames)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {frames.Count} frames; the last emitted:");

            foreach (var frame in frames.TakeLast(6))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"    {frame.Event} {Truncate(frame.Data, 160)}");
            }
        }

        if (responseId is { } runId)
        {
            var store = host.Services.GetRequiredService<IRunStore>();
            var run = await store.GetRunAsync(runId);

            text.AppendLine(CultureInfo.InvariantCulture, $"  response run row: {run?.Status.ToString() ?? "(none; the in-memory run store keeps the latest 1000 runs)"} {run?.Error?.Type}");

            var stored = await store.ReadEventsAsync(runId, 0).ToListAsync();

            text.AppendLine(CultureInfo.InvariantCulture, $"  stored events: {string.Join(", ", stored.Select(static item => $"{item.Sequence} {item.Type}"))}");
        }

        foreach (var entry in host.Logs.Entries.Where(entry =>
                     (entry.StartsWith("Warning ", StringComparison.Ordinal)
                         || entry.StartsWith("Error ", StringComparison.Ordinal)
                         || entry.StartsWith("Critical ", StringComparison.Ordinal))
                     && (entry.Contains(sourceId.ToString(), StringComparison.Ordinal)
                         || (responseId is { } id && entry.Contains(id.ToString(), StringComparison.Ordinal))))
                     .Take(5))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  log {Truncate(entry, 240)}");
        }

        return text.ToString();
    }

    private static async Task Settle(Task abandoned)
    {
        try
        {
            await abandoned;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or HttpRequestException)
        {
            // The stream was abandoned on purpose; how it ends is of no interest.
        }
    }

    private static string Truncate(string text, int length)
        => text.Length <= length ? text : string.Concat(text.AsSpan(0, length), "...");

    private static int Setting(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    /// <summary>
    /// A start step that sends its message to two request ports, so one run publishes two
    /// requests. The same shape as the two-question graph of the workflow unit tests, whose
    /// code registration type is not visible to this project.
    /// </summary>
    private static Workflow BuildTwoQuestions()
    {
        var first = RequestPort.Create<string, bool>(FirstPort).BindAsExecutor(allowWrappedRequests: false);
        var second = RequestPort.Create<string, bool>(SecondPort).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var endFirst = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "first approved" : "first rejected",
            id: "end-first");

        var endSecond = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "second approved" : "second rejected",
            id: "end-second");

        return new WorkflowBuilder(start)
            .AddEdge(start, first)
            .AddEdge(start, second)
            .AddEdge(first, endFirst)
            .AddEdge(second, endSecond)
            .WithOutputFrom(endFirst, endSecond)
            .WithName(Name)
            .Build();
    }

    /// <summary>How the streams that ended as they should ended, for the summary line.</summary>
    private sealed class EndTally
    {
        private int _outputs;
        private int _awaiting;

        public int Outputs => Volatile.Read(ref _outputs);

        public int Awaiting => Volatile.Read(ref _awaiting);

        public void Count(List<SseFrame> frames)
        {
            lock (frames)
            {
                if (frames.Any(static frame => frame.Data.Contains("\"WorkflowOutput\"", StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref _outputs);
                }

                if (frames.Any(static frame => frame.Data.Contains("\"RunAwaitingInput\"", StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref _awaiting);
                }
            }
        }
    }

    /// <summary>How an iteration went wrong.</summary>
    private enum Verdict
    {
        /// <summary>The stream did not end within the bound.</summary>
        Stalled,

        /// <summary>The stream ended with neither the output nor the second question.</summary>
        EndedWithoutResult,

        /// <summary>The iteration threw, or the stream ended without its closing frame.</summary>
        Failed,
    }

    /// <summary>One iteration that did not end as it should.</summary>
    /// <param name="Iteration">The index of the iteration in the batch.</param>
    /// <param name="Verdict">How it went wrong.</param>
    /// <param name="Detail">What the iteration, the stream and the stores held.</param>
    private sealed record Failure(int Iteration, Verdict Verdict, string Detail)
    {
        public override string ToString() => $"{Verdict}: {Detail}";
    }
}
