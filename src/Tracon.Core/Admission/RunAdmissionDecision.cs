namespace Tracon;

/// <summary>Why <see cref="RunAdmission"/> refused a run.</summary>
public enum RunAdmissionDenial
{
    /// <summary>The registered <see cref="IRunAuthorizationHandler"/> denied the run, or threw while deciding.</summary>
    NotAuthorized,

    /// <summary>
    /// The session belongs to another user, or to nobody while unowned sessions are refused. The two answer
    /// alike on purpose: telling them apart would show which sessions predate ownership.
    /// </summary>
    SessionNotAuthorized,

    /// <summary>
    /// Session ownership is on, a session would be opened, and no identity could be resolved to own it.
    /// </summary>
    SessionOwnerRequired,

    /// <summary>A quota of the tenant is spent, or could not be verified and the installation fails closed.</summary>
    QuotaExceeded,
}

/// <summary>The answer of <see cref="RunAdmission"/>: whether a run may start, and if not, why.</summary>
public sealed record RunAdmissionDecision
{
    /// <summary>Gets the decision that lets a run start.</summary>
    public static RunAdmissionDecision Allowed { get; } = new() { IsAllowed = true };

    /// <summary>Gets a value indicating whether the run may start.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>Gets why the run was refused, or <see langword="null"/> when it may start.</summary>
    public RunAdmissionDenial? Denial { get; init; }

    /// <summary>
    /// Gets a human-readable reason, or <see langword="null"/> when the run may start. The text is the one the
    /// HTTP endpoints give for the same refusal. For <see cref="RunAdmissionDenial.NotAuthorized"/> it is the
    /// reason the handler supplied, when it supplied one.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Gets the metric whose limit was reached, or <see langword="null"/> when no limit was reached.</summary>
    public QuotaMetric? QuotaMetric { get; init; }

    /// <summary>Gets the period whose limit was reached, or <see langword="null"/>.</summary>
    public QuotaPeriod? QuotaPeriod { get; init; }

    /// <summary>Gets the limit that was reached, or <see langword="null"/>.</summary>
    public decimal? QuotaLimit { get; init; }

    /// <summary>Gets how much of the limit was used, or <see langword="null"/>.</summary>
    public decimal? QuotaUsed { get; init; }

    /// <summary>Gets when the counter resets, or <see langword="null"/> when it is not known.</summary>
    public DateTimeOffset? QuotaResetsAt { get; init; }

    /// <summary>
    /// Gets the agent the quota names, or <see langword="null"/> for a quota that covers all agents of the tenant.
    /// </summary>
    public string? QuotaAgentName { get; init; }
}
