namespace Tracon;

/// <summary>Defines a query for experiment results.</summary>
public sealed record ExperimentResultsQuery
{
    /// <summary>Gets the identifier of the experiment whose results are requested.</summary>
    public required Guid ExperimentId { get; init; }

    /// <summary>Gets the tenant filter. The current tenant is used when it is empty.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Summarizes run results for an experiment variant (calculated in the store).</summary>
public sealed record ExperimentVariantResult
{
    /// <summary>Gets the variant name.</summary>
    public required string Variant { get; init; }

    /// <summary>Gets the definition version served by the variant.</summary>
    public required int Version { get; init; }

    /// <summary>Gets the total number of runs assigned to this variant.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Gets the number of successfully completed runs.</summary>
    public required long CompletedRuns { get; init; }

    /// <summary>Gets the number of runs that ended with an error.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>Gets the number of cancelled runs.</summary>
    public required long CanceledRuns { get; init; }

    /// <summary>Gets the total input tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>Gets the total output tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Gets the total tokens.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Gets the total cost for this variant. Returns <see langword="null"/> when pricing is undefined.</summary>
    public decimal? TotalCost { get; init; }

    /// <summary>Gets the currency. It is populated when <c>TotalCost</c> is populated.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Gets the average duration of settled runs in milliseconds. Returns
    /// <see langword="null"/> when no run is settled.
    /// </summary>
    public double? AverageDurationMs { get; init; }

    /// <summary>
    /// Gets the error rate among settled runs, from 0 through 1. It uses the same
    /// calculation as <c>RunStatistics.ErrorRate</c>.
    /// </summary>
    public double? ErrorRate
    {
        get
        {
            var settled = CompletedRuns + FailedRuns + CanceledRuns;
            return settled == 0 ? null : (double)FailedRuns / settled;
        }
    }

    /// <summary>
    /// Gets the average numeric score from 0 through 100 for runs assigned to this
    /// variant. It uses the <c>RunScoreKind.Numeric</c> scores written by online evaluation.
    /// Returns <see langword="null"/> when no run is scored. This is not
    /// <c>0</c>; it means unknown, consistent with the existing <c>RunCost</c>
    /// contract.
    /// </summary>
    public double? AverageScore { get; init; }
}
