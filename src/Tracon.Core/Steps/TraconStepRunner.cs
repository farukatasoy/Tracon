using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Runs a step of the host's own code as a run of kind <see cref="RunKind.Step"/>.</summary>
/// <remarks>
/// <para>
/// The third recorder of a run, beside the agent recorder and the workflow runner. It opens the row, the span, the scope and the
/// cancellation registration in the body of the method that calls the step, so the step's code sees them, and it closes the row with
/// the same quota, webhook, metric and trace writes.
/// </para>
/// <para>
/// A step's row has no model, no token usage and no input: what it holds is the non-token items its code reports (the media services,
/// a tool of its own) and the runs below it.
/// </para>
/// </remarks>
internal sealed class TraconStepRunner : ITraconStepRunner
{
    private const int MaxNameLength = 200;

    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    private readonly RunRecordingServices _services;
    private readonly RunCompletionNotifier _notifier;
    private readonly ILogger _logger;

    // The ids of the steps this process has open: a second step that names an id while the first still runs is refused here, before
    // its write would update the first one's row. The store's start is an upsert, so reading the row is not enough on its own.
    private readonly ConcurrentDictionary<Guid, bool> _open = new();

    public TraconStepRunner(RunRecordingServices services, ILogger<TraconStepRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _logger = logger;
        _notifier = new RunCompletionNotifier(services, logger);
    }

    /// <inheritdoc />
    public async ValueTask RunAsync(
        TraconStepOptions options,
        Func<CancellationToken, ValueTask> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        _ = await RunAsync<object?>(
            options,
            async token =>
            {
                await work(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<T> RunAsync<T>(
        TraconStepOptions options,
        Func<CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(work);
        CheckName(options.Name);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_services.Recording.Enabled)
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }

        var tenantId = _services.TenantContext.TenantId;
        var runId = options.RunId ?? TraconId.NewId();

        if (!_open.TryAdd(runId, true))
        {
            throw new TraconException($"A step with run id '{runId}' is already running. A step needs an id of its own.");
        }

        try
        {
            Lineage lineage;

            try
            {
                lineage = await ReadLineageAsync(options, runId, tenantId, cancellationToken).ConfigureAwait(false);
            }
            catch (StoreUnreadableException unreadable)
            {
                // Observability does not break functionality: the ids the step named could not be checked, so the step is not
                // opened as a run, and it runs.
                _logger.LogWarning(
                    unreadable.Cause,
                    "The run store could not be read to open the step '{Name}'; the step runs and is not recorded.",
                    options.Name);

                return await work(cancellationToken).ConfigureAwait(false);
            }

            return await RunRecordedAsync(options.Name, runId, tenantId, lineage, work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _open.TryRemove(runId, out _);
        }
    }

    private static void CheckName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"A step name cannot exceed {MaxNameLength} characters.", nameof(name));
        }

        if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]) || name.Any(char.IsControl))
        {
            throw new ArgumentException("A step name cannot be surrounded by whitespace or hold control characters.", nameof(name));
        }
    }

    /// <summary>Checks the ids a step names before anything is written.</summary>
    private async ValueTask<Lineage> ReadLineageAsync(
        TraconStepOptions options,
        Guid runId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (options.RunId is not null
            && await ReadRunAsync(runId, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new TraconException($"A run with id '{runId}' already exists. A step needs an id of its own.");
        }

        if (options.ParentRunId is not { } parentId)
        {
            return new Lineage(runId, 0, null, null, null);
        }

        var parent = await ReadRunAsync(parentId, cancellationToken).ConfigureAwait(false);

        // 🚨 A run of another tenant answers exactly as a run that does not exist: attaching to it would put this tenant's cost
        // into the other tenant's tree.
        if (parent is null || !string.Equals(parent.TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new TraconException($"There is no run '{parentId}' to attach the step to.");
        }

        var rootId = parent.RootRunId ?? parent.Id;
        var root = rootId == parent.Id
            ? parent
            : await ReadRunAsync(rootId, cancellationToken).ConfigureAwait(false) ?? parent;

        return new Lineage(rootId, parent.Depth + 1, root.AgentName, root.UserId, parentId);
    }

    /// <summary>Reads a run, and reports a store that cannot answer as <see cref="StoreUnreadableException"/>.</summary>
    private async ValueTask<RunRecord?> ReadRunAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await _services.RunStore.GetRunAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            throw new StoreUnreadableException(exception);
        }
    }

    /// <summary>Opens the run, calls the step inside it and closes it.</summary>
    /// <remarks>
    /// Everything ambient (the span and the run scope) is opened in THIS body, the one that calls the step: both are held in an
    /// <c>AsyncLocal</c>, and an assignment made in a method that returns before the step runs would not reach it. The caller's own
    /// values come back when this method returns.
    /// </remarks>
    private async ValueTask<T> RunRecordedAsync<T>(
        string name,
        Guid runId,
        string tenantId,
        Lineage lineage,
        Func<CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken)
    {
        var enclosing = TraconRunContext.Current;
        var attribution = RunAttributionReader.Read(
            _services.AttributionContext,
            (message, exception) => _logger.LogWarning(
                exception,
                "{Message} Step run {RunId} continues and is recorded with no user and no labels.",
                message,
                runId));

        var activity = ActivitySource.StartActivity(TraconDiagnostics.RunActivityName, ActivityKind.Internal);

        // The trace of a step that no run of this process encloses is its own to close; one inside another run's scope belongs to
        // that run's tree.
        var ownsTrace = activity is not null && enclosing is null;

        if (activity is not null)
        {
            activity.SetTag(TraconDiagnostics.Tags.RunId, runId);
            activity.SetTag(TraconDiagnostics.Tags.AgentName, name);
            activity.SetTag(TraconDiagnostics.Tags.TenantId, tenantId);

            if (lineage.ParentRunId is { } parent)
            {
                activity.SetTag(TraconDiagnostics.Tags.ParentRunId, parent);
                activity.SetTag(TraconDiagnostics.Tags.Depth, lineage.Depth);
            }

            if (ownsTrace)
            {
                _services.TraceCollector?.BeginRun(activity.TraceId.ToString(), activity.SpanId.ToString());
            }
        }

        var writer = _services.CreateWriter(runId, _logger, runIdIsNew: true);

        // A step a tool opens inside the run it names as its parent spends against that run's ceiling: the tree has one budget,
        // and a step with a budget of its own would let a run's tools spend past its limit.
        var budget = enclosing is { Budget: { } treeBudget }
            && lineage.ParentRunId == enclosing.RunId
            && string.Equals(enclosing.TenantId, tenantId, StringComparison.Ordinal)
                ? treeBudget
                : null;

        if (budget is null)
        {
            budget = _services.AgentGraph.CreateBudget(_services.TimeProvider);

            // A step that joins a tree adds to the root's counter, under the root's name and user, like every run of the tree.
            budget.Account = new TreeAccount(
                tenantId,
                lineage.RootName ?? name,
                lineage.RootUserId ?? attribution.UserId,
                _services.QuotaEnforcer);
        }

        var scope = new AgentRunScope
        {
            RunId = runId,
            RootRunId = lineage.RootRunId,
            Depth = lineage.Depth,
            AgentName = name,
            TenantId = tenantId,
            UserId = attribution.UserId,
            Labels = attribution.Labels,
            Budget = budget,
            Writer = writer,
            NonTokenCost = new NonTokenCostAccumulator(_services.PricingCurrency, budget),
            IsStep = true,
        };

        TraconRunContext.SetCurrent(scope);

        // POST /api/runs/{id}/cancel reaches the step through its own source; the caller's token stays separate.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var registration = _services.CancellationRegistry?.Register(runId, lineage.RootRunId, tenantId, cancellationSource);

        var startedAt = _services.TimeProvider.GetTimestamp();
        var recording = new RunRecordingScope(
            writer,
            activity,
            new ToolInvocationTracker(runId, measureDuration: false, _services.TimeProvider),
            name,
            tenantId,
            startedAt,
            budget,
            ExtraUsage: null,
            ownsTrace,
            AgentVersion: null,
            runId,
            lineage.RootRunId,
            lineage.Depth,
            SessionId: null,
            RunKind.Step,
            FallbackAttribution: null,
            attribution.UserId,
            scope.NonTokenCost);

        var status = RunStatus.Completed;
        RunError? error = null;

        try
        {
            await writer.StartAsync(
                new RunStartInfo
                {
                    RunId = runId,
                    AgentName = name,
                    Kind = RunKind.Step,
                    StartedAt = _services.TimeProvider.GetUtcNow(),
                    TenantId = tenantId,
                    UserId = attribution.UserId,
                    Labels = attribution.Labels,
                    ParentRunId = lineage.ParentRunId,

                    // Empty on a root run, as the agent recorder writes it.
                    RootRunId = lineage.Depth == 0 ? null : lineage.RootRunId,
                    Depth = lineage.Depth,
                },
                query: null,
                cancellationSource.Token).ConfigureAwait(false);

            return await work(cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            status = RunStatus.Canceled;

            throw;
        }
        catch (Exception exception)
        {
            status = RunStatus.Failed;

            var correlationId = SafeErrorText.NewCorrelationId();
            _logger.LogError(exception, "Step run {RunId} ({Name}) failed. (ref: {CorrelationId})", runId, name, correlationId);

            error = new RunError
            {
                Type = SafeErrorText.TypeOf(exception),
                Message = SafeErrorText.ForPersistence(exception, correlationId),
            };

            throw;
        }
        finally
        {
            await CompleteAsync(recording, status, error, _services.TimeProvider.GetElapsedTime(startedAt)).ConfigureAwait(false);
        }
    }

    // Not the caller's token anywhere below: a run that was canceled still has to be closed.
    private async ValueTask CompleteAsync(RunRecordingScope scope, RunStatus status, RunError? error, TimeSpan elapsed)
    {
        if (error is not null && _services.ErrorClassifier is not null)
        {
            var classification = Classify(error);
            error = error with { Class = classification.Class, Fingerprint = classification.Fingerprint };
        }

        var nonTokenCost = scope.NonTokenCost?.Seal();
        var own = RunCompletionNotifier.OwnConsumption(usage: null, cost: null, nonTokenCost);

        await _notifier.RecordQuotaAsync(scope, own, cost: null, nonTokenCost, CancellationToken.None).ConfigureAwait(false);

        var tree = scope.Depth == 0
            ? await _notifier.ReadTreeConsumptionAsync(scope, own, CancellationToken.None).ConfigureAwait(false)
            : default;

        await scope.Writer.CompleteAsync(
            status,
            usage: null,
            error,
            cost: null,
            nonTokenCost: nonTokenCost,
            modelId: null,
            modelProvider: null,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        if (scope.Depth == 0)
        {
            await _notifier.PublishRunEventAsync(scope, status, tree, error, elapsed, modelId: null, CancellationToken.None)
                .ConfigureAwait(false);
        }

        _services.Metrics?.RecordRun(scope.AgentName, status, scope.TenantId, modelId: null, elapsed, usage: null);

        if (scope.Activity is not { } activity)
        {
            return;
        }

        activity.SetTag(TraconDiagnostics.Tags.Status, status.ToString());

        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Message);
        }

        // Stopped before the collector reads it: stopping is what puts the root span into the buffer.
        activity.Stop();

        if (scope.OwnsTrace && _services.TraceCollector is not null)
        {
            await _services.TraceCollector.CompleteRunAsync(
                activity.TraceId.ToString(),
                activity.SpanId.ToString(),
                scope.RunId,
                scope.TenantId,
                status,
                CancellationToken.None).ConfigureAwait(false);
        }

        activity.Dispose();
    }

    private RunErrorClassification Classify(RunError error)
    {
        try
        {
            return _services.ErrorClassifier!.Classify(error);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The registered IRunErrorClassifier threw while classifying a step run error; falling back to the built-in classifier.");

            return new DefaultRunErrorClassifier().Classify(error);
        }
    }

    /// <summary>The run store could not answer a question the step's ids asked; the step runs unrecorded.</summary>
    private sealed class StoreUnreadableException(Exception cause) : Exception("The run store could not be read.", cause)
    {
        /// <summary>Gets what the store threw.</summary>
        public Exception Cause { get; } = cause;
    }

    /// <summary>Where a step sits in a tree.</summary>
    private sealed record Lineage(Guid RootRunId, int Depth, string? RootName, string? RootUserId, Guid? ParentRunId);
}
