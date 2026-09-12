namespace Tracon.Embedded;

/// <summary>The host's own request shape for creating a support ticket — not a Tracon type.</summary>
internal sealed record CreateTicketRequest(string TenantId, string? UserId, string Subject);
