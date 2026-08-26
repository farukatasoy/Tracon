namespace AgentPrism.Embedded;

/// <summary>The host's own request shape for creating a support ticket — not an AgentPrism type.</summary>
internal sealed record CreateTicketRequest(string TenantId, string? UserId, string Subject);
