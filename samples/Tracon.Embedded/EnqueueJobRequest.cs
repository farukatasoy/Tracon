namespace Tracon.Embedded;

/// <summary>The host's own request shape for enqueuing background work — not an Tracon type.</summary>
internal sealed record EnqueueJobRequest(string TenantId, string? UserId, string Message);
