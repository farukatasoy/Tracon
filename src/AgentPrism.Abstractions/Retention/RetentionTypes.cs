namespace AgentPrism;

/// <summary>An age- and volume-based retention rule for a target.</summary>
/// <remarks>
/// If no record exists, nothing is deleted for the target (see
/// <c>AgentPrismRetentionOptions</c>' configuration-based defaults — they
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
    /// <see cref="MaxRows"/>, if set, applies).
    /// </summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>
    /// The maximum number of rows to keep in the target table. The OLDEST
    /// rows over the limit are deleted. If <see langword="null"/>,
    /// volume-based deletion does not apply (only <see cref="MaxAgeDays"/>,
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
    public bool Enabled { get; init; } = true;

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

/// <summary>A preview of "how many rows would be deleted if run now" for a target.</summary>
public sealed record RetentionPreview
{
    /// <summary>The previewed target.</summary>
    public required string Target { get; init; }

    /// <summary><see langword="null"/> if no policy was found (nothing would be deleted).</summary>
    public int? MaxAgeDays { get; init; }

    /// <summary>Whether the policy is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>The computed cutoff date (UTC). <see langword="null"/> if there is no policy.</summary>
    public DateTimeOffset? Cutoff { get; init; }

    /// <summary>The number of rows currently older than the cutoff date.</summary>
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
