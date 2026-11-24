using System.ComponentModel;

namespace Tracon;

/// <summary>An age- and volume-based retention rule for a target.</summary>
/// <remarks>
/// If no record exists, nothing is deleted for the target (see
/// <c>TraconRetentionOptions</c>' configuration-based defaults — they
/// take effect only when the database has NO record).
/// </remarks>
public sealed record RetentionPolicy
{
    /// <summary>The policy identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant identifier. <c>"*"</c> means the default for all tenants.</summary>
    public required string TenantId { get; init; }

    /// <summary>The target table name. See <see cref="RetentionTargets"/>.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// Rows older than this age are candidates for deletion. If
    /// <see langword="null"/>, age-based deletion does not apply (only
    /// <c>MaxRows</c>, if set, applies).
    /// </summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>
    /// The maximum number of rows to keep in the target table. The OLDEST
    /// rows over the limit are deleted. If <see langword="null"/>,
    /// volume-based deletion does not apply (only <c>MaxAgeDays</c>,
    /// if set, applies).
    /// </summary>
    public long? MaxRows { get; init; }

    /// <summary>
    /// Whether to archive with <c>IArchiveSink</c> before deleting. If no
    /// sink is registered, no row is deleted even if this field is
    /// <see langword="true"/>.
    /// </summary>
    public bool Archive { get; init; }

    /// <summary>Whether the policy is enabled.</summary>
    /// <remarks>A record that does not say is off, as the save request is: a policy deletes rows, so a record that should run says so.</remarks>
    public bool Enabled { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The history record of a cleanup run.</summary>
public sealed record RetentionRun
{
    /// <summary>The run identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The target processed.</summary>
    public required string Target { get; init; }

    /// <summary>The number of rows deleted so far.</summary>
    public long DeletedRows { get; init; }

    /// <summary>The number of rows archived so far.</summary>
    public long ArchivedRows { get; init; }

    /// <summary>The start time (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The completion time (UTC). <see langword="null"/> while the run is in progress.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The error message. Populated only for failed runs.</summary>
    public string? Error { get; init; }
}

/// <summary>What a retention run would delete now for one target of the current tenant.</summary>
/// <remarks>
/// Returned by <see cref="IRetentionPreviewService"/> and by <c>GET /api/retention/preview</c>. It is a reading: it
/// deletes nothing.
/// </remarks>
[Description("A preview of \"how many rows would be deleted if run now\" for a target.")]
public sealed record RetentionPreview
{
    /// <summary>The previewed target. See <see cref="RetentionTargets"/>.</summary>
    [Description("The previewed target.")]
    public required string Target { get; init; }

    /// <summary>The age in days past which a row is deleted. <see langword="null"/> when no policy applies.</summary>
    [Description("`null` if no policy was found (nothing would be deleted).")]
    public int? MaxAgeDays { get; init; }

    /// <summary>Whether a policy that deletes applies to the target for this tenant.</summary>
    [Description("Whether the policy is enabled.")]
    public bool Enabled { get; init; }

    /// <summary>The cutoff date (UTC): rows older than it would be deleted. <see langword="null"/> when no policy applies.</summary>
    [Description("The computed cutoff date (UTC). `null` if there is no policy.")]
    public DateTimeOffset? Cutoff { get; init; }

    /// <summary>
    /// The number of rows a run would delete now: the rows older than <c>Cutoff</c>. For the <c>attachments</c>
    /// target without an <c>IAttachmentStorage</c> it leaves out the rows that point at external content, because a
    /// run keeps them.
    /// </summary>
    [Description("The number of rows a run would delete now: the rows older than the cutoff date, without the attachment rows that point at external content when no attachment storage is registered.")]
    public long MatchingRows { get; init; }
}

/// <summary>The JSON representation of a single row to write to the archive.</summary>
/// <remarks>
/// The row carries no schema information: column names and values sit
/// directly inside a JSON object (one line of JSONL). This way, archiving
/// does not need to know the target's column set ahead of time.
/// </remarks>
public sealed record ArchiveRow
{
    /// <summary>The row's single-line JSON representation (has NO trailing newline).</summary>
    public required string Json { get; init; }
}
