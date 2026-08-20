namespace AgentPrism;

/// <summary>
/// The limits a run's attribution must obey, and the single place they are enforced.
/// </summary>
/// <remarks>
/// <para>
/// Labels answer "which job was this run made for". They are a small
/// <c>string</c>-to-<c>string</c> map, kept deliberately small: a run label is
/// a query dimension, and an unbounded set of them turns every breakdown query
/// into a scan over an unbounded key space.
/// </para>
/// <para>
/// Breaking a limit REJECTS the value; it is never trimmed. A trimmed label
/// set still looks like a complete measurement to whoever reads the report
/// later, and that is worse than an error at the boundary.
/// </para>
/// </remarks>
public static class RunLabels
{
    /// <summary>The largest number of labels one run may carry.</summary>
    public const int MaxCount = 8;

    /// <summary>The largest length of a label key.</summary>
    /// <remarks>A key is usable as a breakdown dimension, so it stays short enough to group by.</remarks>
    public const int MaxKeyLength = 64;

    /// <summary>The largest length of a label value.</summary>
    /// <remarks>This is not a free-text store; longer content belongs in the run's own event stream.</remarks>
    public const int MaxValueLength = 256;

    /// <summary>The largest length of a user identity.</summary>
    /// <remarks>
    /// The value is stored in an INDEXED column. SQL Server cannot index
    /// <c>nvarchar(max)</c>, so every indexed key column in this schema is
    /// bounded at 200 characters; the limit here matches that column.
    /// </remarks>
    public const int MaxUserIdLength = 200;

    /// <summary>
    /// Checks a complete attribution.
    /// </summary>
    /// <param name="userId">The user identity, or <see langword="null"/>.</param>
    /// <param name="labels">The labels, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="null"/> when the value is acceptable, otherwise a message
    /// naming the limit that was broken.
    /// </returns>
    public static string? Validate(string? userId, IReadOnlyDictionary<string, string>? labels)
        => ValidateUserId(userId) ?? ValidateLabels(labels);

    /// <summary>Checks a user identity on its own.</summary>
    /// <param name="userId">The user identity, or <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the value is acceptable, otherwise a message.</returns>
    public static string? ValidateUserId(string? userId)
        => userId is { Length: > MaxUserIdLength }
            ? $"The user identity is {userId.Length} characters long; at most {MaxUserIdLength} are allowed."
            : null;

    /// <summary>Checks a label set on its own.</summary>
    /// <param name="labels">The labels, or <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the value is acceptable, otherwise a message.</returns>
    public static string? ValidateLabels(IReadOnlyDictionary<string, string>? labels)
    {
        if (labels is null or { Count: 0 })
        {
            return null;
        }

        if (labels.Count > MaxCount)
        {
            return $"The run carries {labels.Count} labels; at most {MaxCount} are allowed.";
        }

        foreach (var (key, value) in labels)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "A label key is empty. Every label needs a key.";
            }

            if (key.Length > MaxKeyLength)
            {
                return $"The label key '{Describe(key)}' is {key.Length} characters long; at most {MaxKeyLength} are allowed.";
            }

            if (value is null)
            {
                return $"The label '{key}' has no value. Use an empty string when the value is not known.";
            }

            if (value.Length > MaxValueLength)
            {
                return $"The value of label '{key}' is {value.Length} characters long; at most {MaxValueLength} are allowed.";
            }
        }

        return null;
    }

    /// <summary>
    /// Copies a label set into an immutable, ordinally compared dictionary.
    /// </summary>
    /// <param name="labels">The labels to copy.</param>
    /// <returns>A snapshot that later writes to the source cannot change.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="labels"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The caller's dictionary may be mutable and may even be a live request
    /// object; a run record must not change under the reader after the run ends.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var copy = new Dictionary<string, string>(labels.Count, StringComparer.Ordinal);

        foreach (var (key, value) in labels)
        {
            copy[key] = value;
        }

        return copy;
    }

    // A broken key is echoed back to the caller; a very long one is cut so the
    // error message stays readable in a log line.
    private static string Describe(string key)
        => key.Length <= MaxKeyLength ? key : string.Concat(key.AsSpan(0, MaxKeyLength), "...");
}
