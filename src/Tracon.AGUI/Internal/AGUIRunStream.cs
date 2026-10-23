using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Writes the AG-UI event stream of an opened turn as the response: <c>text/event-stream</c>, one <c>data:</c> frame per event.
/// </summary>
/// <remarks>
/// <para>
/// A frame carries no <c>event:</c> name and no <c>id:</c>. An AG-UI client reads the event type from the JSON, and a named
/// frame is one an <c>EventSource</c> listener for the default <c>message</c> event never sees.
/// </para>
/// <para>
/// Everything that can still be refused with a status code has been refused before this is built, so by the time it runs the
/// status line is on the wire (K-324) and a failure can only be an event in the stream.
/// </para>
/// </remarks>
internal sealed class AGUIRunStream(
    OpenedTurn opened,
    RunAgentInput input,
    Guid runId,
    IReadOnlyList<string> ignoredTools) : IResult
{
    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cancellationToken = httpContext.RequestAborted;
        var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);

        // Stops the writer's keep-alive loop before the response ends (F-325).
        await using var writerScope = writer.ConfigureAwait(false);

        var started = false;

        try
        {
            await foreach (var aguiEvent in AGUIEventStream
                               .RunAsync(opened, input, runId, httpContext, cancellationToken)
                               .ConfigureAwait(false))
            {
                started |= aguiEvent is RunStartedEvent;

                await writer.WriteEventAsync(id: null, eventName: null, Serialize(aguiEvent), cancellationToken).ConfigureAwait(false);
            }
        }
        // 🚨 `when (RequestAborted)`, not a bare catch: the provider's own request timeout is a TaskCanceledException too,
        // and the client is still connected and waiting for it (Phase 157).
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected. No one is left to write to.
        }
        catch (Exception exception)
        {
            // Nothing goes uncaught (K-296): a closed connection WITHOUT a RUN_ERROR reads as a silent success.
            var (type, message) = AgentTurnService.DescribeStreamFailure(exception, runId, httpContext);

            if (!started)
            {
                // A front end rejects an error that comes before the run started.
                await writer.WriteEventAsync(
                    id: null,
                    eventName: null,
                    Serialize(new RunStartedEvent { ThreadId = input.ThreadId, RunId = input.RunId, ProtocolVersion = AGUIProtocol.Version }),
                    CancellationToken.None).ConfigureAwait(false);
            }

            await writer.WriteEventAsync(
                id: null,
                eventName: null,
                Serialize(new RunErrorEvent { Code = type, Message = message }),
                CancellationToken.None).ConfigureAwait(false);
        }

        await RecordIgnoredToolsAsync(httpContext).ConfigureAwait(false);
    }

    private static string Serialize(BaseEvent aguiEvent)
        => JsonSerializer.Serialize(aguiEvent, AGUIJsonSerializerContext.Default.BaseEvent);

    /// <summary>
    /// Writes the notice that the front end declared tools the agent does not carry, into the run's own event stream.
    /// </summary>
    /// <remarks>
    /// Best effort and after the stream: observability does not break functionality, and a run that is not recorded has no
    /// stream to write into. The notice carries the names and nothing else, not the schema or the description the front end sent.
    /// </remarks>
    private async ValueTask RecordIgnoredToolsAsync(HttpContext httpContext)
    {
        if (ignoredTools.Count == 0)
        {
            return;
        }

        try
        {
            var runs = httpContext.RequestServices.GetRequiredService<IRunStore>();
            var lastSequence = await runs.GetLastEventSequenceAsync(runId, CancellationToken.None).ConfigureAwait(false);

            var names = ignoredTools
                .Take(AGUIInputMapper.MaxIgnoredToolNames)
                .Select(static name => name.Length > AGUIInputMapper.MaxIgnoredToolNameLength
                    ? name[..AGUIInputMapper.MaxIgnoredToolNameLength]
                    : name)
                .ToArray();

            await runs.AppendEventAsync(
                new RunEvent
                {
                    RunId = runId,
                    Sequence = (lastSequence ?? -1) + 1,
                    Type = RunEventType.Custom,
                    CustomType = RunEventCustomTypes.AGUIToolsIgnored,
                    Timestamp = DateTimeOffset.UtcNow,
                    Text = $"{ignoredTools.Count} AG-UI tool(s) ignored",
                    Payload = JsonSerializer.Serialize(new { tools = names }),
                    TenantId = httpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId,
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.AGUI")
                .LogWarning(exception, "The ignored AG-UI tools of run {RunId} could not be recorded.", runId);
        }
    }
}
