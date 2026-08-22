namespace AgentPrism.Embedded;

/// <summary>
/// One unit of the host's own background work — the shape a queued email
/// digest, a nightly summary, or a webhook-triggered task would take.
/// </summary>
/// <param name="TenantId">The tenant this job runs for. Neither fixed nor from an HTTP request.</param>
/// <param name="UserId">The user the job was requested by, or <see langword="null"/> for a system job.</param>
/// <param name="Message">The instruction given to the agent.</param>
internal sealed record EmbeddedJob(string TenantId, string? UserId, string Message);
