using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Maintains a sliding-window summary of online evaluation scores and publishes
/// <see cref="WebhookEvents.RunScoreLow"/> when its threshold is crossed — phase 49.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The score window is <strong>in memory</strong>, with one queue per tenant.
/// The <c>run_scores</c> table is always the source of truth; an operator can
/// query its exact result at any time. This service is only an inexpensive live
/// indicator and alarm, and resets when the process restarts. The same design is
/// used for <see cref="RunSampler"/>'s hourly budget (K1: no durable counter store).
/// </para>
/// <para>
/// 🚨 One low score does not produce an alarm. Threshold evaluation does not run
/// before <see cref="OnlineEvaluationOptions.MinSampleSize"/> is met. Models are
/// noisy, and otherwise notifications quickly become ignored.
/// </para>
/// </remarks>
public sealed class OnlineEvalSummaryService(
    IRunStore runStore,
    IOptionsMonitor<OnlineEvaluationOptions> optionsMonitor,
    IWebhookPublisher? webhookPublisher = null,
    TimeProvider? timeProvider = null)
{
    private readonly ConcurrentDictionary<string, TenantWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a newly written judge score to the window and publishes
    /// <see cref="WebhookEvents.RunScoreLow"/> when needed.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="score">The written score, from 0 to 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask RecordScoreAsync(string tenantId, int score, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var window = _windows.GetOrAdd(tenantId, static _ => new TenantWindow());

        ScoreWindowSnapshot snapshot;

        lock (window)
        {
            window.Add(now, score, options.EvaluationWindow);
            snapshot = window.Summarize(now, options.EvaluationWindow);
        }

        var belowThreshold = snapshot.SampleCount >= options.MinSampleSize &&
            snapshot.AverageScore is { } average && average < options.LowScoreThreshold;

        if (!belowThreshold || webhookPublisher is null)
        {
            return;
        }

        await webhookPublisher.PublishAsync(
            tenantId,
            WebhookEvents.RunScoreLow,
            new WebhookEventPayload
            {
                OccurredAt = now,
                Score = new WebhookScoreSummary
                {
                    AverageScore = snapshot.AverageScore!.Value,
                    SampleCount = snapshot.SampleCount,
                    Threshold = options.LowScoreThreshold,
                    WindowStart = now - options.EvaluationWindow,
                    WindowEnd = now,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a tenant's current window summary and judge cost.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<OnlineEvaluationSummary> GetSummaryAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var since = now - options.EvaluationWindow;

        ScoreWindowSnapshot snapshot = default;

        if (_windows.TryGetValue(tenantId, out var window))
        {
            lock (window)
            {
                snapshot = window.Summarize(now, options.EvaluationWindow);
            }
        }

        // 🚨 Judge runs are recorded as `AgentName = "judge:{name}"` (see
        // ModelRunJudge). This prefix distinguishes them from evaluation-suite
        // case runs that share the Kind.Eval value. The existing RunQuery.Kind
        // filter is sufficient and does not require a second SQL query surface.
        var evalRuns = await runStore.QueryRunsAsync(
            new RunQuery
            {
                TenantId = tenantId,
                Kind = RunKind.Eval,
                StartedAfter = since,
                OnlyRootRuns = false,
                Take = 500,
            },
            cancellationToken).ConfigureAwait(false);

        decimal? judgeCost = null;
        string? judgeCostCurrency = null;

        foreach (var run in evalRuns)
        {
            if (!run.AgentName.StartsWith("judge:", StringComparison.Ordinal) ||
                run.Cost is not { Source: not PricingSource.Unknown } cost)
            {
                continue;
            }

            judgeCost = (judgeCost ?? 0m) + (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m);
            judgeCostCurrency ??= cost.Currency;
        }

        return new OnlineEvaluationSummary
        {
            WindowStart = since,
            WindowEnd = now,
            SampleCount = snapshot.SampleCount,
            AverageScore = snapshot.AverageScore,
            LowScoreThreshold = options.LowScoreThreshold,
            MinSampleSize = options.MinSampleSize,
            BelowThreshold = snapshot.SampleCount >= options.MinSampleSize &&
                snapshot.AverageScore is { } avg && avg < options.LowScoreThreshold,
            JudgeCost = judgeCost,
            JudgeCostCurrency = judgeCostCurrency,
        };
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ScoreWindowSnapshot(long SampleCount, double? AverageScore);

    private sealed class TenantWindow
    {
        private readonly Queue<(DateTimeOffset At, int Score)> _samples = new();

        public void Add(DateTimeOffset at, int score, TimeSpan windowSize)
        {
            _samples.Enqueue((at, score));
            Prune(at, windowSize);
        }

        public ScoreWindowSnapshot Summarize(DateTimeOffset now, TimeSpan windowSize)
        {
            Prune(now, windowSize);

            return _samples.Count == 0
                ? default
                : new ScoreWindowSnapshot(_samples.Count, _samples.Average(static sample => sample.Score));
        }

        private void Prune(DateTimeOffset now, TimeSpan windowSize)
        {
            var cutoff = now - windowSize;

            while (_samples.Count > 0 && _samples.Peek().At < cutoff)
            {
                _samples.Dequeue();
            }
        }
    }
}

/// <summary>Summarizes the online evaluation window (<c>GET /api/evaluation/online</c>).</summary>
public sealed record OnlineEvaluationSummary
{
    /// <summary>Gets the window start in UTC.</summary>
    public required DateTimeOffset WindowStart { get; init; }

    /// <summary>Gets the window end in UTC.</summary>
    public required DateTimeOffset WindowEnd { get; init; }

    /// <summary>Gets the sample count, which is the number of scored runs in the window.</summary>
    public required long SampleCount { get; init; }

    /// <summary>Gets the average score in the window, from 0 to 100, or <see langword="null"/> with no samples.</summary>
    public double? AverageScore { get; init; }

    /// <summary>Gets the configured low-score threshold.</summary>
    public required int LowScoreThreshold { get; init; }

    /// <summary>Gets the minimum sample count required for an alarm.</summary>
    public required int MinSampleSize { get; init; }

    /// <summary>
    /// Gets <see langword="true"/> when the average is below the threshold and
    /// the sample count meets the minimum.
    /// </summary>
    public required bool BelowThreshold { get; init; }

    /// <summary>Gets the total judge cost in the window, or <see langword="null"/> when unknown.</summary>
    public decimal? JudgeCost { get; init; }

    /// <summary>Gets the judge-cost currency.</summary>
    public string? JudgeCostCurrency { get; init; }
}
