using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Validation for the <c>CustomType</c> string that qualifies a
/// <see cref="RunEventType.Custom"/> event.
/// </summary>
/// <remarks>
/// Repeats the <see cref="JobHandlerKeys"/> pattern: a namespaced, lowercase
/// ASCII string with a reserved prefix Tracon itself never emits, so a
/// future built-in custom type can never collide with a consumer's own.
/// </remarks>
public static partial class RunEventCustomTypes
{
    /// <summary>The prefix reserved for Tracon's own use.</summary>
    /// <remarks>
    /// A consumer's <c>CustomType</c> starting with this prefix is rejected
    /// at write time — not because Tracon ships a built-in custom type
    /// today, but so one could be added later without ever colliding with a
    /// consumer's own.
    /// </remarks>
    public const string ReservedPrefix = "tracon.";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the quota threshold
    /// notice a root run's completion writes into its own event stream.
    /// </summary>
    /// <remarks>
    /// Falls under <see cref="ReservedPrefix"/>: a consumer cannot write an
    /// event under this type, so a client can trust that every event carrying
    /// it came from Tracon itself, not from the agent's own tool code.
    /// Off by default (<c>TraconQuotaOptions.PublishThresholdToRunStream</c>).
    /// </remarks>
    public const string QuotaThreshold = ReservedPrefix + "quota.threshold";

    /// <summary>
    /// The <see cref="RunEventType.Custom"/> type of the notice a run writes when
    /// the model stopped a plain-text response at its output token limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run does not fail: a partial text answer is still useful, so the run
    /// keeps its status and this event says the answer is incomplete. A structured
    /// (JSON) response cut off the same way fails its run with
    /// <see cref="RunErrorClass.OutputTruncated"/> instead, because a partial JSON
    /// document has no use.
    /// </para>
    /// <para>
    /// The payload carries the provider name, what cut the answer off
    /// (<c>cause</c>: <c>output_limit</c> or <c>context_window</c>) and, when the
    /// binding set one, the output token limit. It never carries the response text.
    /// Written only for the agent's own model calls, not for a side-channel call such
    /// as a context summarizer or a loop judge.
    /// </para>
    /// <para>
    /// The live stream of an agent run (<c>POST /api/agents/{name}/run</c> without
    /// <c>Idempotency-Key</c>) also carries this event, always: it arrives as a
    /// <c>custom</c> frame right before <c>done</c>, with the same data
    /// <c>GET /api/runs/{id}/events</c> serves. Each recorded event is one frame, so a
    /// harness loop whose iterations were cut sends more than one. No event is
    /// written, and no frame is sent, when run recording is off.
    /// </para>
    /// </remarks>
    public const string OutputTruncated = ReservedPrefix + "output.truncated";

    /// <summary>
    /// Checks whether <paramref name="customType"/> is a valid custom event
    /// type: 1-128 characters, lowercase ASCII letters, digits, <c>.</c>,
    /// <c>_</c>, or <c>-</c>, starting with a letter or digit.
    /// </summary>
    /// <param name="customType">The candidate type.</param>
    /// <returns><see langword="true"/> if the type is valid.</returns>
    /// <remarks>
    /// Uppercase letters are rejected, not normalized — the same reasoning as
    /// <see cref="JobHandlerKeys.IsValidKey"/>. A consumer that read back its
    /// own event compares the type ordinally, so allowing case variants would
    /// let <c>"Contoso.Preview"</c> and <c>"contoso.preview"</c> silently
    /// become two different types.
    /// </remarks>
    public static bool IsValidType(string? customType) => customType is not null && TypePattern().IsMatch(customType);

    /// <summary>
    /// Checks whether <paramref name="customType"/> falls inside the
    /// <see cref="ReservedPrefix"/> namespace.
    /// </summary>
    /// <param name="customType">The candidate type.</param>
    /// <returns><see langword="true"/> if the type is reserved for Tracon.</returns>
    public static bool IsReserved(string? customType)
        => customType is not null && customType.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TypePattern();
}
