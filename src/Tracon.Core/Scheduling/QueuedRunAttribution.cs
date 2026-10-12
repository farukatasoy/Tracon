using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The attribution a background run replays: from a queued agent run's
/// <c>labels</c> field, from a workflow or agent batch payload's envelope
/// (<see cref="JobPayload"/>), or from the record of the run it continues.
/// </summary>
/// <remarks>
/// <para>
/// A worker has no request to resolve a user or labels from, so it replays
/// the value captured where the work started, through
/// <see cref="AmbientRunAttributionScope"/>. Everything under the run reads the
/// scope: its sub-agents' run rows, the tools (through
/// <see cref="TraconRunContext"/>), tool authorization and the quota record.
/// </para>
/// <para>
/// Both sources are stored data. A payload can be written by hand through a
/// schedule, and a record by a third-party store. A part that breaks a limit on
/// <see cref="RunLabels"/> is therefore DROPPED, never thrown on:
/// <see cref="AmbientRunAttributionScope.Begin"/> rejects it, and a throw
/// would fail the job on every attempt and never recover. A label set is
/// dropped whole, never trimmed.
/// </para>
/// <para>
/// This type only READS. The scope itself is opened in the handler's own
/// <c>ExecuteAsync</c> body: an <see cref="AsyncLocal{T}"/> written inside an
/// async helper does not flow back to its caller.
/// </para>
/// </remarks>
internal static class QueuedRunAttribution
{
    /// <summary>
    /// Reads the user and labels a workflow or agent batch job's payload names in
    /// its envelope (<see cref="JobPayload"/>).
    /// </summary>
    /// <param name="job">The queued job.</param>
    /// <param name="logger">The handler's logger; <see langword="null"/> logs nothing.</param>
    /// <returns>
    /// The payload's content when it names a user or a label; otherwise
    /// <see langword="null"/>, and the handler opens no scope.
    /// </returns>
    /// <remarks>
    /// An envelope that is malformed or breaks a limit is dropped and logged rather
    /// than thrown on. The boundaries that write a payload refuse such an envelope
    /// with a reason; one that still reaches the worker was written straight to a
    /// store, and failing the job on every retry would never recover it.
    /// </remarks>
    public static JobPayloadContent? Read(JobRecord job, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(job);

        var content = JobPayload.Read(job.HandlerKey, job.Payload);

        if (content.Problem is { } problem)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "Job {JobId}: {Problem} Its runs are recorded with no user and no labels.",
                    job.Id,
                    problem);
            }

            return null;
        }

        return content.HasAttribution ? content : null;
    }

    /// <summary>Reads a job payload's optional <c>labels</c> object.</summary>
    /// <param name="payload">The job payload; an object.</param>
    /// <param name="problem">
    /// Why the field was dropped, or <see langword="null"/> when it is absent or valid.
    /// </param>
    /// <returns>
    /// The labels, or <see langword="null"/> when the field is absent, empty or dropped.
    /// </returns>
    public static IReadOnlyDictionary<string, string>? ReadLabels(JsonElement payload, out string? problem)
    {
        problem = null;

        if (!payload.TryGetProperty("labels", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            problem = "The job payload's 'labels' field is not an object of string values.";

            return null;
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                problem = $"The job payload's 'labels' field is not an object of string values; '{property.Name}' is not a string.";

                return null;
            }

            labels[property.Name] = property.Value.GetString()!;
        }

        if (RunLabels.ValidateLabels(labels) is { } error)
        {
            problem = error;

            return null;
        }

        return labels.Count > 0 ? labels : null;
    }

    /// <summary>
    /// The user and labels of the run a resumed or continued run continues,
    /// with any part that breaks a limit dropped.
    /// </summary>
    /// <param name="source">The run being continued.</param>
    /// <returns>The attribution to open the scope with.</returns>
    public static (string? UserId, IReadOnlyDictionary<string, string>? Labels) FromRecord(RunRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var userId = string.IsNullOrWhiteSpace(source.UserId) || RunLabels.ValidateUserId(source.UserId) is not null
            ? null
            : source.UserId;

        var labels = source.Labels is { Count: > 0 } recorded && RunLabels.ValidateLabels(recorded) is null
            ? recorded
            : null;

        return (userId, labels);
    }
}
