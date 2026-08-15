namespace AgentPrism;

/// <summary>
/// The <c>After</c> payload for an <c>experiment.auto_rollback</c> audit entry.
/// </summary>
internal sealed record CanaryRollbackAuditPayload
{
    /// <summary>The human-readable reason for the rollback.</summary>
    public required string Reason { get; init; }

    /// <summary>The canary arm error rate at rollback time.</summary>
    public double? CanaryErrorRate { get; init; }

    /// <summary>The control arm error rate at rollback time.</summary>
    public double? ControlErrorRate { get; init; }

    /// <summary>The canary arm average score at rollback time.</summary>
    public double? CanaryAverageScore { get; init; }
}
