namespace AgentPrism;

/// <summary>Defines queued, durable run options — phase 46.</summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:AsyncRun</c> configuration section.
/// </para>
/// <para>
/// 🚨 <strong>Enabled is the default</strong>, as with phase 43's
/// <c>Idempotency-Key</c>. A request without <c>Prefer: respond-async</c> has
/// no additional cost or behavioral change; it does not issue a query. If this
/// were disabled by default, a client that sent the header would believe the
/// run was queued when it was not, which would be the real silent surprise.
/// </para>
/// </remarks>
public sealed class AgentPrismAsyncRunOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AgentPrism:AsyncRun";

    /// <summary>
    /// Gets or sets whether <c>Prefer: respond-async</c> is recognized. When
    /// disabled, a request that carries the header receives <c>501</c> rather
    /// than silently falling back to synchronous execution. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum attempt count for a queued run.
    /// </summary>
    /// <remarks>
    /// 🚨 Defaults to <strong>1</strong> to prevent a side-effecting tool from
    /// running twice when a lease expires and the job is reclaimed. Raising it
    /// requires idempotent tools. "Durable" means a job does not disappear
    /// silently, not that it can never be lost.
    /// </remarks>
    public int MaxAttempts { get; set; } = 1;
}
