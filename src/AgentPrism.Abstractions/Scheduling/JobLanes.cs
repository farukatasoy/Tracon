using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>Well-known values and validation for job <c>lane</c> identifiers.</summary>
/// <remarks>
/// A <c>lane</c> is a plain string tag on a job (<see cref="JobRecord.Lane"/>); it
/// has no registry or lifecycle of its own, and it is NOT the job's dispatch
/// identity (that is <see cref="JobRecord.HandlerKey"/>). Worker instances subscribe to a
/// subset of lanes (<c>AgentPrismSchedulingOptions.Lanes</c>) to avoid
/// head-of-line blocking between unrelated kinds of work.
/// </remarks>
public static partial class JobLanes
{
    /// <summary>The lane every job carries unless a different one is set explicitly.</summary>
    public const string Default = "default";

    /// <summary>
    /// Checks whether <paramref name="name"/> is a valid lane name: 1-64
    /// characters, lowercase ASCII letters, digits, <c>.</c>, <c>_</c>, or
    /// <c>-</c>, starting with a letter or digit.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    /// <remarks>
    /// Uppercase letters are rejected, not normalized. Lane names are compared
    /// ordinally everywhere (queries, worker subscriptions); allowing case
    /// variants would let <c>"Media"</c> and <c>"media"</c> silently become two
    /// different lanes, with the second one's jobs never leased by a worker
    /// that only knows about the first.
    /// </remarks>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <summary>
    /// Resolves the lane a job is enqueued under, applying
    /// <c>AgentPrismSchedulingOptions.LaneByHandlerKey</c> when
    /// <paramref name="lane"/> is still <see cref="Default"/>, then validates
    /// the result.
    /// </summary>
    /// <param name="lane">The lane requested by the caller (<see cref="JobRecord.Lane"/>).</param>
    /// <param name="handlerKey">The job's handler key (<see cref="JobRecord.HandlerKey"/>).</param>
    /// <param name="laneByHandlerKey">The configured key-to-lane map. May be <see langword="null"/> or empty.</param>
    /// <returns>The resolved, valid lane name.</returns>
    /// <exception cref="ArgumentException">The resolved name is not a valid lane name.</exception>
    /// <remarks>
    /// Called from every <c>IJobStore.EnqueueAsync</c> implementation — the
    /// one place every job, from every call site, passes through. A caller
    /// that already set an explicit, non-default lane (an HTTP request body,
    /// or a schedule's own <see cref="JobSchedule.Lane"/>) is never overridden.
    /// </remarks>
    public static string Resolve(string lane, string handlerKey, IDictionary<string, string>? laneByHandlerKey)
    {
        var resolved = lane;

        if (string.Equals(resolved, Default, StringComparison.Ordinal)
            && laneByHandlerKey is { Count: > 0 }
            && laneByHandlerKey.TryGetValue(handlerKey, out var mapped))
        {
            resolved = mapped;
        }

        if (!IsValidName(resolved))
        {
            throw new ArgumentException(
                $"'{resolved}' is not a valid lane name. A lane name must be 1-64 characters: lowercase " +
                "ASCII letters, digits, '.', '_', or '-', starting with a letter or digit.",
                nameof(lane));
        }

        return resolved;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamePattern();
}
