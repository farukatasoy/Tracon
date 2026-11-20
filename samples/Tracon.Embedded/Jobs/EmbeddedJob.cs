namespace Tracon.Embedded;

/// <summary>
/// One unit of the host's own background work — the shape a queued email
/// digest, a nightly summary, or a webhook-triggered task would take.
/// </summary>
/// <param name="TenantId">The tenant this job runs for. Neither fixed nor from an HTTP request.</param>
/// <param name="UserId">The user the job was requested by, or <see langword="null"/> for a system job.</param>
/// <param name="Message">The instruction given to the agent.</param>
/// <param name="ArticleContent">
/// Text the job hands the <c>summarizer</c> agent as its <c>articleContent</c> parameter, or
/// <see langword="null"/> for a job that runs the <c>assistant</c> agent.
/// </param>
internal sealed record EmbeddedJob(string TenantId, string? UserId, string Message, string? ArticleContent = null);
