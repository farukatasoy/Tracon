using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Checks the attribution an <see cref="IRunAttributionContext"/> resolved
/// before a run starts, and produces a <c>400</c> when it breaks a limit.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The limits on <see cref="RunLabels"/> are enforced by REJECTING the
/// request, never by trimming it. A trimmed label set still reads as a complete
/// measurement to whoever queries the cost report later, so silently dropping
/// the ninth label would corrupt the data it is meant to describe.
/// </para>
/// <para>
/// The check is called <strong>explicitly</strong> at every endpoint that starts
/// a run rather than being an endpoint filter, for the same reason
/// <see cref="QuotaGate"/> is: the endpoints that start runs do not share one
/// route shape.
/// </para>
/// <para>
/// This is the LOUD boundary. The recording path has a second, quiet guard that
/// drops an oversized attribution and logs it, because observability must not
/// break a run that is already under way — reaching that one means the request
/// never passed through here.
/// </para>
/// </remarks>
internal static class RunAttributionGate
{
    /// <summary>Checks the current attribution; produces the response to return if it is invalid.</summary>
    /// <param name="attribution">
    /// The attribution context. When <see langword="null"/> no check is
    /// performed, which is also the outcome for an application that registers
    /// nothing.
    /// </param>
    /// <returns>
    /// The <c>400</c> response to return when a limit is broken; otherwise
    /// <see langword="null"/>.
    /// </returns>
    public static IResult? Check(IRunAttributionContext? attribution)
    {
        if (attribution is null)
        {
            return null;
        }

        string? userId;
        IReadOnlyDictionary<string, string>? labels;

        try
        {
            userId = attribution.UserId;
            labels = attribution.Labels;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A consumer implementation reaches into its own identity pipeline
            // and can fail there. That failure is not the caller's fault and
            // must not turn into a 400; the recording path logs it and records
            // the run with no attribution.
            return null;
        }

        if (RunLabels.Validate(string.IsNullOrWhiteSpace(userId) ? null : userId, labels) is not { } error)
        {
            return null;
        }

        return Results.Problem(
            title: "Invalid run attribution",
            detail: error +
                    $" At most {RunLabels.MaxCount} labels are allowed, keys are at most " +
                    $"{RunLabels.MaxKeyLength} characters and values at most {RunLabels.MaxValueLength}. " +
                    "The run did not start; labels are never trimmed to fit.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Splits a <c>label=key:value</c> query parameter.
    /// </summary>
    /// <param name="label">The raw query value, or <see langword="null"/>.</param>
    /// <returns>
    /// The key and value to filter on. Both are <see langword="null"/> when no
    /// filter was requested; the value alone is <see langword="null"/> when only
    /// a key was given, which matches any value of that key.
    /// </returns>
    /// <remarks>
    /// The split is on the FIRST colon only, so a value may itself contain
    /// colons (a URN or a timestamp, for example). A trailing bare key such as
    /// <c>label=team</c> is legal and means "carries this key at all".
    /// </remarks>
    public static (string? Key, string? Value) ParseLabelFilter(string? label)
    {
        if (label is not { Length: > 0 })
        {
            return (null, null);
        }

        var separator = label.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            return (label, null);
        }

        var key = label[..separator];

        return key.Length == 0 ? (null, null) : (key, label[(separator + 1)..]);
    }
}
