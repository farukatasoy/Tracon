using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A store that reports its own timeout as an
/// <see cref="OperationCanceledException"/> must not stop the host.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The failure this closes: every periodic service filtered its tick on
/// <c>exception is not OperationCanceledException</c>, and the loop filtered
/// on <c>catch (OperationCanceledException) when
/// (stoppingToken.IsCancellationRequested)</c>. An OCE raised while the
/// service is NOT stopping passes both, faults <c>ExecuteAsync</c>, and .NET's
/// default <c>BackgroundServiceExceptionBehavior</c> (<c>StopHost</c>) takes
/// the process down.
/// </para>
/// <para>
/// The trigger is not hypothetical. <c>IRunStore</c>, <c>IApprovalStore</c>
/// and <c>IJobStore</c> are public extension points with guide pages, and a
/// store written over HTTP surfaces <c>HttpClient.Timeout</c> as
/// <c>TaskCanceledException</c>. The shipped store guide tells the author to
/// throw an OCE. The first-party SQL drivers report a command timeout as a
/// provider exception, so the built-in path never showed this.
/// </para>
/// </remarks>
public sealed class ForeignCancellationTests
{
    [Fact]
    public void A_foreign_cancellation_is_an_ordinary_failure()
    {
        using var stopping = new CancellationTokenSource();

        OperationCancellation
            .IsFailure(new TaskCanceledException("HTTP store timed out"), stopping.Token)
            .ShouldBeTrue();

        OperationCancellation
            .IsFailure(new OperationCanceledException(), stopping.Token)
            .ShouldBeTrue();
    }

    [Fact]
    public void The_operations_own_cancellation_is_not_a_failure()
    {
        using var stopping = new CancellationTokenSource();

        stopping.Cancel();

        // Left to propagate: the loop's own shutdown filter answers it, and the
        // service ends cleanly instead of logging a warning on every stop.
        OperationCancellation
            .IsFailure(new OperationCanceledException(), stopping.Token)
            .ShouldBeFalse();
    }

    [Fact]
    public void An_ordinary_failure_is_a_failure_whether_or_not_the_operation_is_cancelled()
    {
        using var stopping = new CancellationTokenSource();

        OperationCancellation.IsFailure(new InvalidOperationException(), stopping.Token).ShouldBeTrue();

        stopping.Cancel();

        OperationCancellation.IsFailure(new InvalidOperationException(), stopping.Token).ShouldBeTrue();
    }

    [Fact]
    public async Task The_heartbeat_writer_survives_a_store_that_throws_a_foreign_cancellation()
    {
        // The service under test is the one whose own comment states the rule
        // it was breaking: "Observability does not break functionality."
        var store = new CancellingRunStore(new InMemoryRunStore());
        var registry = new RunCancellationRegistry();
        var gate = new SchemaReadyGate([]);

        var writer = new RunHeartbeatWriter(
            store,
            registry,
            new StaticOptionsMonitor<RunReconciliationOptions>(new RunReconciliationOptions
            {
                Enabled = true,
                HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            }),
            gate,
            logger: NullLogger<RunHeartbeatWriter>.Instance);

        var runId = TraconId.NewId();
        using var cancellation = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, "default", cancellation);

        await writer.StartAsync(TestContext.Current.CancellationToken);

        // Bounded, and it also stops as soon as the service dies: a spin that
        // only waited for the third attempt would HANG on the broken build
        // instead of failing, and a test that hangs teaches nothing.
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (store.Attempts < 3
            && DateTime.UtcNow < deadline
            && writer.ExecuteTask?.IsCompleted != true)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        // ExecuteTask is the task ExecuteAsync returned. A faulted task here IS
        // the host stopping: the default BackgroundServiceExceptionBehavior is
        // StopHost.
        var execute = writer.ExecuteTask.ShouldNotBeNull();

        execute.IsFaulted.ShouldBeFalse();
        execute.IsCompleted.ShouldBeFalse();

        // And the loop really kept ticking, rather than merely not faulting.
        store.Attempts.ShouldBeGreaterThanOrEqualTo(3);

        await writer.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CancellingRunStore(IRunStore inner) : IRunStore
    {
        private int _claimCalls;
        private int _touchCalls;

        public int Attempts => Volatile.Read(ref _touchCalls);

        public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
            => inner.StartRunAsync(info, cancellationToken);

        public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => inner.AppendEventAsync(runEvent, cancellationToken);

        public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<long?>(null);

        public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteRunAsync(completion, cancellationToken);

        public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.GetRunAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default)
            => inner.QueryRunsAsync(query, cancellationToken);

        public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken cancellationToken = default)
            => inner.GetStatisticsAsync(query, cancellationToken);

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken cancellationToken = default)
            => inner.ReadEventsAsync(runId, fromSequence, cancellationToken);

        public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken cancellationToken = default)
            => inner.RecordToolInvocationAsync(invocation, cancellationToken);

        public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteLateToolInvocationAsync(completion, cancellationToken);

        public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken cancellationToken = default)
            => inner.ListToolInvocationsAsync(runId, cancellationToken);

        public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken cancellationToken = default)
            => inner.GetToolUsageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken cancellationToken = default)
            => inner.GetExperimentResultsAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken cancellationToken = default)
            => inner.GetTimeSeriesAsync(query, cancellationToken);

        public ValueTask UpdateRunCostAsync(
            Guid runId,
            RunCost? cost,
            string? tenantId = null,
            CancellationToken cancellationToken = default)
            => inner.UpdateRunCostAsync(runId, cost, tenantId, cancellationToken);

        public ValueTask TouchHeartbeatAsync(
            IReadOnlyCollection<Guid> runIds,
            DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _touchCalls);

            // What an HTTP-backed store raises on its own timeout. The service
            // is NOT stopping, so this is a failure, not a shutdown.
            throw new TaskCanceledException("The store timed out.");
        }

        public async ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
            DateTimeOffset staleBefore,
            int max,
            CancellationToken cancellationToken = default)
        {
            var claimed = await inner.ClaimOrphanedRunsAsync(staleBefore, max, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _claimCalls);
            return claimed;
        }
    }
}
