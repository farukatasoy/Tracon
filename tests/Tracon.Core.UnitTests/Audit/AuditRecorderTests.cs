using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// Covers the two audit write guarantees at the one place that now implements them:
/// best-effort swallows, fail-closed refuses, and both count the failure.
/// </summary>
/// <remarks>
/// Phase 171 replaced four hand-written copies of the fail-closed path with this one,
/// plus two direct calls that differed from all of them: one logged a warning and
/// returned instead of throwing, and one skipped the secret filter. The behaviour is
/// pinned here rather than being re-decided at each call site.
/// </remarks>
public sealed class AuditRecorderTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task A_best_effort_write_that_fails_warns_counts_and_returns()
    {
        var logger = new SpyLogger();
        using var meterFactory = new TestMeterFactory();
        using var collector = new MetricCollector(meterFactory.Meter);
        var metrics = new TraconMetrics(meterFactory);

        await AuditRecorder.WriteAsync(
            new ThrowingAuditLog(),
            new FixedActorResolver("operator"),
            logger,
            metrics,
            Tenant,
            action: "agent.update",
            entity: "agent:support",
            before: null,
            after: null,
            CancellationToken.None);

        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        collector.LongValues(TraconDiagnostics.AuditWriteFailureCounterName).ShouldBe([1]);

        var tags = collector.LongTags(TraconDiagnostics.AuditWriteFailureCounterName).Single();
        tags[TraconDiagnostics.Tags.AuditOutcome].ShouldBe("swallowed");
        tags[TraconDiagnostics.Tags.AuditAction].ShouldBe("agent.update");
        tags[TraconDiagnostics.Tags.TenantId].ShouldBe(Tenant);
    }

    [Fact]
    public async Task A_fail_closed_write_that_fails_logs_an_error_counts_and_throws()
    {
        var logger = new SpyLogger();
        using var meterFactory = new TestMeterFactory();
        using var collector = new MetricCollector(meterFactory.Meter);
        var metrics = new TraconMetrics(meterFactory);

        var refusal = await Should.ThrowAsync<TraconException>(async () =>
            await AuditRecorder.WriteOrThrowAsync(
                new ThrowingAuditLog(),
                "operator",
                logger,
                metrics,
                Tenant,
                action: "script.run",
                entity: "notes/build",
                before: null,
                after: null,
                refusal: "Script 'notes/build' was not run",
                timeProvider: null,
                CancellationToken.None));

        refusal.Message.ShouldBe(
            "Script 'notes/build' was not run because it could not be written to the audit trail.");
        refusal.InnerException.ShouldBeOfType<InvalidOperationException>();

        // LogError, not LogWarning. A refused operation is not an observability
        // hiccup: the caller's work did not happen because of it.
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error);

        var tags = collector.LongTags(TraconDiagnostics.AuditWriteFailureCounterName).Single();
        tags[TraconDiagnostics.Tags.AuditOutcome].ShouldBe("refused");
    }

    [Fact]
    public async Task A_successful_write_counts_nothing_on_either_path()
    {
        using var meterFactory = new TestMeterFactory();
        using var collector = new MetricCollector(meterFactory.Meter);
        var metrics = new TraconMetrics(meterFactory);
        var log = new CapturingAuditLog();

        await AuditRecorder.WriteAsync(
            log,
            new FixedActorResolver("operator"),
            NullLogger.Instance,
            metrics,
            Tenant,
            action: "agent.update",
            entity: "agent:support",
            before: null,
            after: null,
            CancellationToken.None);

        await AuditRecorder.WriteOrThrowAsync(
            log,
            "operator",
            NullLogger.Instance,
            metrics,
            Tenant,
            action: "script.run",
            entity: "notes/build",
            before: null,
            after: null,
            refusal: "Script 'notes/build' was not run",
            timeProvider: null,
            CancellationToken.None);

        log.Entries.Count.ShouldBe(2);
        collector.LongValues(TraconDiagnostics.AuditWriteFailureCounterName).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_and_is_not_counted_as_a_failure()
    {
        // A cancelled write is the caller going away, not the audit store breaking.
        // Counting it would raise a false alarm on every shutdown, and swallowing it
        // on the best-effort path would hide the cancellation from the caller.
        using var meterFactory = new TestMeterFactory();
        using var collector = new MetricCollector(meterFactory.Meter);
        var metrics = new TraconMetrics(meterFactory);
        var log = new ThrowingAuditLog(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await AuditRecorder.WriteAsync(
                log,
                new FixedActorResolver("operator"),
                NullLogger.Instance,
                metrics,
                Tenant,
                action: "agent.update",
                entity: "agent:support",
                before: null,
                after: null,
                CancellationToken.None));

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await AuditRecorder.WriteOrThrowAsync(
                log,
                "operator",
                NullLogger.Instance,
                metrics,
                Tenant,
                action: "script.run",
                entity: "notes/build",
                before: null,
                after: null,
                refusal: "Script 'notes/build' was not run",
                timeProvider: null,
                CancellationToken.None));

        collector.LongValues(TraconDiagnostics.AuditWriteFailureCounterName).ShouldBeEmpty();
    }

    [Fact]
    public async Task Both_paths_redact_a_secret_before_it_reaches_the_store()
    {
        var log = new CapturingAuditLog();
        const string Payload = """{"apiKey":"sk-live-should-not-survive"}""";

        await AuditRecorder.WriteAsync(
            log,
            new FixedActorResolver("operator"),
            NullLogger.Instance,
            metrics: null,
            Tenant,
            action: "mcp.create",
            entity: "mcp:github",
            before: null,
            after: Payload,
            CancellationToken.None);

        await AuditRecorder.WriteOrThrowAsync(
            log,
            "operator",
            NullLogger.Instance,
            metrics: null,
            Tenant,
            action: "trigger.create",
            entity: "trigger:github",
            before: Payload,
            after: Payload,
            refusal: "'trigger:github' was not saved",
            timeProvider: null,
            CancellationToken.None);

        string[] written =
        [
            log.Entries[0].After!,
            log.Entries[1].Before!,
            log.Entries[1].After!,
        ];

        written.ShouldAllBe(text => !text.Contains("sk-live-should-not-survive", StringComparison.Ordinal));
        written.ShouldAllBe(text => text.Contains("***", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_fail_closed_path_takes_its_actor_and_clock_from_the_caller()
    {
        // Both exist because of real call sites: the canary evaluator runs on a
        // background timer with no ambient actor to resolve, and the skill script
        // runner stamps its entries with an injected TimeProvider.
        var log = new CapturingAuditLog();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

        await AuditRecorder.WriteOrThrowAsync(
            log,
            "system:canary-evaluator",
            NullLogger.Instance,
            metrics: null,
            Tenant,
            action: "experiment.auto_rollback",
            entity: "experiment:pricing",
            before: null,
            after: null,
            refusal: "The automatic rollback of experiment 'pricing' was not applied",
            clock,
            CancellationToken.None);

        log.Entries.Single().Actor.ShouldBe("system:canary-evaluator");
        log.Entries.Single().CreatedAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public async Task An_empty_refusal_is_rejected_before_anything_is_written()
    {
        var log = new CapturingAuditLog();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await AuditRecorder.WriteOrThrowAsync(
                log,
                "operator",
                NullLogger.Instance,
                metrics: null,
                Tenant,
                action: "script.run",
                entity: "notes/build",
                before: null,
                after: null,
                refusal: "   ",
                timeProvider: null,
                CancellationToken.None));

        log.Entries.ShouldBeEmpty();
    }

    private sealed class ThrowingAuditLog(Exception? failure = null) : IAuditLog
    {
        private readonly Exception _failure = failure ?? new InvalidOperationException("audit store is unreachable");

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw _failure;

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by this test");
    }

    private sealed class CapturingAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Entries);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by this test");
    }

    private sealed class FixedActorResolver(string actor) : IAuditActorResolver
    {
        public string? Resolve() => actor;
    }

    private sealed class SpyLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
