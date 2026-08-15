namespace AgentPrism;

/// <summary>The filter used to list attachments.</summary>
public sealed record AttachmentQuery
{
    /// <summary>Gets the tenant to query.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the session whose attachments are returned, when it is given.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>Gets the maximum number of records to return.</summary>
    public int Take { get; init; } = 50;
}
