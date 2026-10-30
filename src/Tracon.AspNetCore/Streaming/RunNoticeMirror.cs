using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Sends the reserved notices a run wrote into its own recorded event stream on the
/// agent run's live SSE connection, as <c>custom</c> frames right before <c>done</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why it reads the record back.</strong> The live stream forwards only
/// Microsoft Agent Framework's own <see cref="Microsoft.Agents.AI.AgentResponseUpdate"/>s
/// as <c>update</c> frames; an event written through the run's writer never passes
/// through that path. When the update loop ends, the run has closed and every notice
/// is already written. Reading it back gives this connection the same frame
/// <c>GET /api/runs/{id}/events</c> serves, byte for byte, instead of a second copy
/// built on its own.
/// </para>
/// <para>
/// <strong>The list is closed.</strong> Only three types are sent:
/// <see cref="RunEventCustomTypes.OutputTruncated"/> and
/// <see cref="RunEventCustomTypes.OutputFiltered"/> always, and
/// <see cref="RunEventCustomTypes.QuotaThreshold"/> only while
/// <see cref="TraconQuotaOptions.PublishThresholdToRunStream"/> is on. A consumer's
/// own <see cref="RunEventType.Custom"/> event, and every other reserved type, stays
/// on <c>/events</c> only. Adding a type here changes the wire contract of the run
/// endpoint; it is a contract decision, not a local change.
/// </para>
/// <para>
/// <strong>The store is read only when a notice can exist.</strong> The quota notice
/// can exist only while its option is on. The truncation and the filter notice can exist only when
/// an update the endpoint forwarded reported a finish reason that may leave an incomplete answer
/// (<see cref="FinishReasonDetectingChatClient.IsIncomplete"/>, the one predicate: a cut-off
/// or a content filter).
/// Microsoft Agent Framework copies each model call's finish reason onto the update,
/// and the detector writes the event from the same reason. A tool
/// turn cut at the limit opens the scan too, but the detector judges only the final
/// turn and writes nothing; that false positive costs one read. (A filtered answer with
/// no content normally fails the run, so the stream ends in an <c>error</c> and never reaches this type.)
/// A run that reported neither and has the quota option off does not read the store at all.
/// </para>
/// <para>
/// <strong>A store failure does not fail the stream.</strong> The run already
/// completed; the failure is logged as a warning and the stream ends with
/// <c>done</c> and without the notices; an <c>error</c> frame would tell the client
/// that a completed run failed. A cancelled request stops the read and the
/// cancellation goes on to the endpoint.
/// </para>
/// <para>
/// Only the request's own run is read, through the store's tenant filter; the
/// mirror opens no new tenant path.
/// </para>
/// </remarks>
internal static class RunNoticeMirror
{
    // The frame name is always "custom", whatever the event's own type; the
    // reading side was built on that and later widened it.

    /// <summary>The SSE event name of every mirrored notice, whatever its custom type.</summary>
    internal const string FrameName = "custom";

    // The same settings GET /api/runs/{id}/events serializes a recorded event with;
    // the frames must match byte for byte (OutputTruncationStreamingTests).
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes a recorded event the way <c>GET /api/runs/{id}/events</c> does, for every frame of the run
    /// stream that mirrors one.
    /// </summary>
    /// <param name="runEvent">The recorded event.</param>
    /// <returns>The JSON of the frame's <c>data</c> field.</returns>
    internal static string Serialize(RunEvent runEvent) => JsonSerializer.Serialize(runEvent, JsonOptions);

    /// <summary>
    /// Reads the run's notices back and writes each one as a <c>custom</c> frame, in
    /// record order.
    /// </summary>
    /// <param name="httpContext">The request, for the run store, the options and the logger.</param>
    /// <param name="runId">The run the stream belongs to.</param>
    /// <param name="writer">The stream's writer.</param>
    /// <param name="sequence">The id of the next frame.</param>
    /// <param name="sawIncomplete">
    /// Whether an update the endpoint forwarded reported a cut-off or content-filter finish reason
    /// (<see cref="FinishReasonDetectingChatClient.IsIncomplete"/>).
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The id of the frame after the last one written.</returns>
    internal static async ValueTask<long> WriteAsync(
        HttpContext httpContext,
        Guid runId,
        SseWriter writer,
        long sequence,
        bool sawIncomplete,
        CancellationToken cancellationToken)
    {
        var quotaNotices = httpContext.RequestServices
            .GetService<IOptionsMonitor<TraconQuotaOptions>>()?.CurrentValue.PublishThresholdToRunStream is true;

        if (!quotaNotices && !sawIncomplete)
        {
            return sequence;
        }

        var notices = await ReadAsync(httpContext, runId, quotaNotices, cancellationToken).ConfigureAwait(false);

        foreach (var notice in notices)
        {
            await writer.WriteEventAsync(
                sequence++,
                FrameName,
                Serialize(notice),
                cancellationToken).ConfigureAwait(false);
        }

        return sequence;
    }

    /// <summary>Tells whether a recorded event is on the closed list.</summary>
    /// <param name="runEvent">The recorded event.</param>
    /// <param name="quotaNotices">Whether the quota notice is sent on the run stream.</param>
    /// <returns><see langword="true"/> when the event is sent on the live stream.</returns>
    internal static bool IsMirrored(RunEvent runEvent, bool quotaNotices)
        => runEvent.Type == RunEventType.Custom && runEvent.CustomType switch
        {
            RunEventCustomTypes.OutputTruncated => true,
            RunEventCustomTypes.OutputFiltered => true,
            RunEventCustomTypes.QuotaThreshold => quotaNotices,
            _ => false,
        };

    /// <summary>Reads the notices, or none when the store fails.</summary>
    /// <remarks>
    /// The read finishes before the first frame is written, so a write failure (the
    /// client went away) is never mistaken for a store failure.
    /// </remarks>
    private static async ValueTask<List<RunEvent>> ReadAsync(
        HttpContext httpContext,
        Guid runId,
        bool quotaNotices,
        CancellationToken cancellationToken)
    {
        var notices = new List<RunEvent>();

        try
        {
            var runs = httpContext.RequestServices.GetRequiredService<IRunStore>();

            await foreach (var runEvent in runs.ReadEventsAsync(runId, 0, cancellationToken).ConfigureAwait(false))
            {
                if (IsMirrored(runEvent, quotaNotices))
                {
                    notices.Add(runEvent);
                }
            }
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RunNoticeMirror))
                .LogWarning(ex, "The notices of run {RunId} could not be read; the stream ends without them.", runId);

            return [];
        }

        return notices;
    }
}
