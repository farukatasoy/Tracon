using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Runs an opened turn and returns its AG-UI event stream.
/// </summary>
/// <remarks>
/// <para>
/// The agent's updates go through the AG-UI event converter (<c>AsAGUIEventStreamAsync</c>); this class only prepares what
/// goes into it and corrects what comes out of it. It adds nothing of its own to the protocol except one <c>CUSTOM</c> event
/// that carries the Tracon run id.
/// </para>
/// <para>
/// The session is saved before the stream ends, so a save that loses a race becomes the run's error and
/// <c>RUN_FINISHED</c> is never sent for a turn the session does not hold.
/// </para>
/// </remarks>
internal static class AGUIEventStream
{
    /// <summary>The name of the <c>CUSTOM</c> event that carries the Tracon run id.</summary>
    internal const string RunEventName = "tracon.run";

    // The options the converter serializes the raw update of an event with. The interrupt content has to be registered, or
    // the first interrupt fails the stream with an error that says nothing.
    private static readonly JsonSerializerOptions ConverterOptions = CreateConverterOptions();

    /// <summary>Runs the turn.</summary>
    /// <param name="opened">The opened turn.</param>
    /// <param name="input">The request body; its thread and run identities are the ones the events carry.</param>
    /// <param name="runId">The Tracon run.</param>
    /// <param name="httpContext">The request, for the logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The AG-UI events, <c>RUN_STARTED</c> first.</returns>
    public static async IAsyncEnumerable<BaseEvent> RunAsync(
        OpenedTurn opened,
        RunAgentInput input,
        Guid runId,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var collector = new AGUIInterruptCollector();
        var failure = new StreamFailure();

        // The converter reads the thread and run identities from here. The conversation itself is the session's, so the
        // request's messages, tools and context are not handed over: converting them costs the whole history on every turn.
        var context = new RunAgentInput
        {
            ThreadId = input.ThreadId,
            RunId = input.RunId,
            Messages = [],
            Tools = [],
            Context = [],
        }.ToChatRequestContext(ConverterOptions, new AGUIStreamOptions());

        var updates = AdaptAsync(opened, collector, failure, runId, httpContext, cancellationToken);
        var runEventSent = false;

        await foreach (var aguiEvent in updates.AsAGUIEventStreamAsync(context, cancellationToken).ConfigureAwait(false))
        {
            // The converter hides the cause of a failure from the front end; the text written here carries the
            // correlation id the log line has.
            if (aguiEvent is RunErrorEvent error && failure.Value is { } known)
            {
                error.Code = known.Type;
                error.Message = known.Message;
            }

            yield return aguiEvent;

            if (!runEventSent && aguiEvent is RunStartedEvent)
            {
                runEventSent = true;

                yield return new CustomEvent
                {
                    Name = RunEventName,
                    Value = JsonSerializer.SerializeToElement(new { runId, threadId = input.ThreadId }),
                };
            }
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> AdaptAsync(
        OpenedTurn opened,
        AGUIInterruptCollector collector,
        StreamFailure failure,
        Guid runId,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = opened.RunStreamingAsync(collector.OnApprovalsAsync, collector.OnInputsAsync, cancellationToken);
        var enumerator = source.GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                AgentResponseUpdate update;

                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception exception) when (IsFailure(exception, cancellationToken))
                {
                    failure.Value = AgentTurnService.DescribeStreamFailure(exception, runId, httpContext);

                    throw;
                }

                yield return collector.Adapt(update.AsChatResponseUpdate());
            }
        }

        try
        {
            await opened.SaveSessionAsync(httpContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFailure(exception, cancellationToken))
        {
            failure.Value = AgentTurnService.DescribeStreamFailure(exception, runId, httpContext);

            throw;
        }

        if (collector.BuildInterrupts(runId) is { } interrupts)
        {
            yield return interrupts;
        }
    }

    // 🚨 A request timeout of the provider's HttpClient is a TaskCanceledException too, and the client is still waiting for
    // it. Only a cancellation the client asked for is not a failure (Phase 157).
    private static bool IsFailure(Exception exception, CancellationToken cancellationToken)
        => exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static JsonSerializerOptions CreateConverterOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions);

        AGUIJsonUtilities.RegisterInterruptContentTypes(options);

        return options;
    }

    /// <summary>What the stream failed with, once it has.</summary>
    private sealed class StreamFailure
    {
        public (string Type, string Message)? Value { get; set; }
    }
}
