namespace Tracon;

/// <summary>A single quota threshold crossed by a period's consumption.</summary>
/// <remarks>
/// <para>
/// Returned by <c>QuotaEnforcer.RecordAsync</c> for every threshold that a
/// completed root run's consumption newly crossed — "newly" meaning the
/// durable per-period dedup (<c>IQuotaStore</c>) claimed it for the first
/// time; a threshold already claimed by an earlier run or a concurrent
/// worker is not returned again.
/// </para>
/// <para>
/// A single completion can cross more than one threshold at once (for
/// example both the 80% and the 100% mark of the same metric, or the
/// <see cref="QuotaMetric.Tokens"/> and <see cref="QuotaMetric.Cost"/>
/// metrics of the same rule) — each crossing is reported separately, one
/// notice per metric/threshold pair.
/// </para>
/// </remarks>
public sealed record QuotaThresholdCrossing
{
    /// <summary>
    /// The dedup/delivery identifier for this crossing. Stable for the
    /// lifetime of the notice: a client that reads it again (through
    /// <c>Last-Event-ID</c> resumption or <c>GET /api/runs/{id}/events</c>)
    /// sees the same value and can suppress a duplicate warning.
    /// </summary>
    public required string NoticeId { get; init; }

    /// <summary>The metric whose threshold was crossed.</summary>
    public required QuotaMetric Metric { get; init; }

    /// <summary>The counter's interval.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>The crossed threshold percentage (for example 80 or 100).</summary>
    public required int ThresholdPercent { get; init; }

    /// <summary>The rule's defined limit.</summary>
    public required decimal Limit { get; init; }

    /// <summary>The consumption at the moment the threshold was claimed.</summary>
    public required decimal Used { get; init; }

    /// <summary>The agent the rule is bound to. <see langword="null"/> for a tenant-wide rule.</summary>
    public string? AgentName { get; init; }

    /// <summary>The time the counter resets (UTC).</summary>
    public DateTimeOffset? ResetsAt { get; init; }
}
