namespace AgentPrism;

/// <summary>A data subject's exported content.</summary>
public sealed record DataSubjectExport
{
    /// <summary>
    /// Gets the exported content, as a single JSON document (UTF-8 text). Object
    /// keys are the target table names (<c>sessions</c>, <c>runs</c>, and so on),
    /// each holding the array of that target's matching rows exactly as stored.
    /// </summary>
    /// <remarks>
    /// Attachment file bytes are NOT included — only their metadata (name, media
    /// type, size, hash) is. A byte payload can be large and belongs in a
    /// different export shape (a zip of files) than a JSON document; that is a
    /// separate, unscheduled work item.
    /// </remarks>
    public required string Json { get; init; }
}
