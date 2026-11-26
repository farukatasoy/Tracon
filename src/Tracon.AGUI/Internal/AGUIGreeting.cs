using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The opening request of an AG-UI conversation: the sentence the assistant says first, written to the history by the host's
/// own code and streamed back, with no model call and no run.
/// </summary>
/// <remarks>
/// <para>
/// By the time this runs the request has passed the same gates a run passes and the agent is resolved for the tenant, so a
/// request that is turned away never reaches the host's code.
/// </para>
/// <para>
/// The greeting is said once. A thread that already has a history, a thread another opening request wrote to first and a
/// resolver that says nothing all answer with <c>RUN_STARTED</c> and <c>RUN_FINISHED</c> alone.
/// </para>
/// </remarks>
internal static class AGUIGreeting
{
    /// <summary>Opens the conversation of a thread.</summary>
    /// <param name="turn">The prepared turn; only its agent is used, nothing is run.</param>
    /// <param name="input">The request body.</param>
    /// <param name="forwarded">What the front end sent in <c>forwardedProps.parameters</c>; untrusted.</param>
    /// <param name="resolveGreeting">The host's greeting resolver.</param>
    /// <param name="httpContext">The request.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>The refusal or the event stream.</returns>
    public static async Task<IResult> OpenAsync(
        PreparedTurn turn,
        RunAgentInput input,
        IReadOnlyDictionary<string, string>? forwarded,
        Func<AGUIParameterRequest, CancellationToken, ValueTask<string?>> resolveGreeting,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var sessions = httpContext.RequestServices.GetRequiredService<AgentSessionManager>();
        string? greeting = null;

        try
        {
            // `null` is a thread with no session record, `[]` one with a record and no messages: both are new conversations.
            var history = await sessions
                .ReadHistoryAsync(turn.Agent, input.ThreadId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (history is not { Count: > 0 })
            {
                var text = await ResolveAsync(
                    resolveGreeting,
                    turn.AgentName,
                    input.ThreadId,
                    forwarded,
                    httpContext,
                    cancellationToken).ConfigureAwait(false);

                if (text.Problem is { } resolverProblem)
                {
                    return resolverProblem;
                }

                if (!string.IsNullOrWhiteSpace(text.Value))
                {
                    var session = await sessions
                        .GetOrCreateSessionAsync(turn.Agent, input.ThreadId, cancellationToken)
                        .ConfigureAwait(false);

                    // The host's code may have taken its time, and another opening request of the thread may have written while
                    // it did. The history is read again now that the session is open, and the save that follows is guarded by
                    // the session's version: of two openings, one writes and the other says nothing.
                    var current = await sessions
                        .ReadHistoryAsync(turn.Agent, input.ThreadId, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (current is not { Count: > 0 })
                    {
                        await AgentTurnService
                            .RecordAssistantMessageAsync(sessions, turn.Agent, session, text.Value, cancellationToken)
                            .ConfigureAwait(false);

                        greeting = text.Value;
                    }
                }
            }
        }
        catch (TraconSessionConflictException)
        {
            // Another opening request of the same thread wrote first; that one said the greeting.
            greeting = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected before anything was written.
            return Results.Empty;
        }
        catch (Exception exception)
        {
            return AgentTurnService.DescribeFailure(exception, turn.RunId, httpContext);
        }

        return new AGUIGreetingStream(input, greeting);
    }

    /// <summary>Runs the host's greeting resolver; a failure never carries the exception's text into the answer.</summary>
    private static async ValueTask<(IResult? Problem, string? Value)> ResolveAsync(
        Func<AGUIParameterRequest, CancellationToken, ValueTask<string?>> resolveGreeting,
        string agentName,
        string threadId,
        IReadOnlyDictionary<string, string>? forwarded,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await resolveGreeting(
                    new AGUIParameterRequest(
                        agentName,
                        threadId,
                        httpContext.User,
                        forwarded ?? new Dictionary<string, string>(StringComparer.Ordinal)),
                    cancellationToken)
                .ConfigureAwait(false);

            return (null, text);
        }
        catch (Exception exception) when (AGUIEventStream.IsFailure(exception, cancellationToken))
        {
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Tracon.AGUI")
                .LogError(exception, "The AG-UI greeting resolver failed for agent '{AgentName}', thread {ThreadId}; nothing was written.", agentName, threadId);

            return (
                Results.Problem(
                    title: "The opening could not be resolved",
                    detail: "The host's greeting resolver failed, so nothing was written to the conversation.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["errorType"] = TraconErrorTypes.GreetingResolutionFailed,
                    }),
                null);
        }
    }
}

/// <summary>
/// Writes the events of an opening request: <c>RUN_STARTED</c>, the greeting as one text message when there is one, and
/// <c>RUN_FINISHED</c>.
/// </summary>
/// <remarks>
/// There is no <c>tracon.run</c> event: no run exists, so there is no run id to carry. A front end that waits for it before it
/// shows the text would wait for nothing; the text message is what it shows.
/// </remarks>
internal sealed class AGUIGreetingStream(RunAgentInput input, string? greeting) : IResult
{
    /// <inheritdoc />
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cancellationToken = httpContext.RequestAborted;
        var writer = await SseWriter.StartAsync(httpContext.Response, cancellationToken).ConfigureAwait(false);

        await using var writerScope = writer.ConfigureAwait(false);

        try
        {
            await WriteAsync(writer, new RunStartedEvent { ThreadId = input.ThreadId, RunId = input.RunId, ProtocolVersion = AGUIProtocol.Version }, cancellationToken)
                .ConfigureAwait(false);

            if (greeting is not null)
            {
                var messageId = Guid.NewGuid().ToString("N");

                await WriteAsync(writer, new TextMessageStartEvent { MessageId = messageId, Role = "assistant" }, cancellationToken).ConfigureAwait(false);
                await WriteAsync(writer, new TextMessageContentEvent { MessageId = messageId, Delta = greeting }, cancellationToken).ConfigureAwait(false);
                await WriteAsync(writer, new TextMessageEndEvent { MessageId = messageId }, cancellationToken).ConfigureAwait(false);
            }

            await WriteAsync(
                writer,
                new RunFinishedEvent { ThreadId = input.ThreadId, RunId = input.RunId, Outcome = new RunFinishedSuccessOutcome() },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected. The greeting is already in the history.
        }
    }

    private static Task WriteAsync(SseWriter writer, BaseEvent aguiEvent, CancellationToken cancellationToken)
        => writer.WriteEventAsync(
            id: null,
            eventName: null,
            JsonSerializer.Serialize(aguiEvent, AGUIJsonSerializerContext.Default.BaseEvent),
            cancellationToken);
}
