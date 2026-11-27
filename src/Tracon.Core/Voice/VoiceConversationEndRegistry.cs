using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Remembers, for every turn a conversation connection runs, how that turn asks the connection to end after its answer.
/// </summary>
/// <remarks>
/// <para>
/// The <c>end_conversation</c> tool runs inside the agent's run and knows only the run it belongs to (its root run). The
/// connection that started that run is the one that has to close, so the connection registers the run before it starts and
/// the tool finds it here. A run that no connection registered (an HTTP run of the same agent) finds nothing and ends
/// nothing.
/// </para>
/// <para>
/// A run identifier is unique and chosen by the connection, so one connection can never end another's conversation: the tool
/// can only name the run it is itself running in.
/// </para>
/// </remarks>
internal sealed class VoiceConversationEndRegistry
{
    private readonly ConcurrentDictionary<Guid, Action> _turns = new();

    /// <summary>Registers a turn's end request until the returned lease is disposed.</summary>
    /// <param name="runId">The run the turn is about to start.</param>
    /// <param name="requestEnd">What the connection does when the turn asks to end the conversation.</param>
    /// <returns>A lease that removes the registration.</returns>
    public IDisposable Register(Guid runId, Action requestEnd)
    {
        ArgumentNullException.ThrowIfNull(requestEnd);

        _turns[runId] = requestEnd;

        return new Lease(this, runId);
    }

    /// <summary>Asks the connection that runs <paramref name="runId"/> to end its conversation after the answer.</summary>
    /// <param name="runId">The root run of the calling tool.</param>
    /// <returns><see langword="true"/> when a connection owns the run.</returns>
    public bool TryRequestEnd(Guid runId)
    {
        if (!_turns.TryGetValue(runId, out var requestEnd))
        {
            return false;
        }

        requestEnd();

        return true;
    }

    /// <summary>Gets the number of registered turns; the tests use it to prove a closed connection leaves none behind.</summary>
    internal int Count => _turns.Count;

    private sealed class Lease(VoiceConversationEndRegistry registry, Guid runId) : IDisposable
    {
        public void Dispose() => registry._turns.TryRemove(runId, out _);
    }
}

/// <summary>
/// The <c>end_conversation</c> tool: lets the agent end the voice conversation it is running in, after it has said its last
/// words.
/// </summary>
/// <remarks>
/// <para>
/// The tool is registered by <c>UseVoiceConversation</c> and the agent has to name it in its <c>ToolNames</c>; an agent that
/// does not can never end a conversation. It takes no argument and ends nothing outside a voice conversation.
/// </para>
/// <para>
/// A model that says goodbye in the answer that carries the call has nothing left to say, and asking it again would cost the
/// caller a second model request for an empty answer. The tool therefore stops the function-invocation loop when the
/// response that carried its call already holds words, and lets the loop go on when it holds none: the model then says its
/// goodbye in the next request, as the text of the tool's result asks.
/// </para>
/// </remarks>
internal sealed class EndConversationTool(VoiceConversationEndRegistry registry) : AIFunction
{
    /// <summary>The tool's name, as an agent lists it in <c>ToolNames</c>.</summary>
    public const string ToolName = "end_conversation";

    internal const string EndingText = "The conversation will end after this answer. Say a short goodbye now.";

    internal const string NotAConversationText = "This run is not a voice conversation, so nothing was ended.";

    private const string Schema = """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """;

    /// <inheritdoc />
    public override string Name => ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Ends the voice conversation after your answer has been spoken. Call it when the conversation is over " +
        "(the topic is done, the time is up, or the caller said goodbye). Say a short goodbye first and call this tool " +
        "after it, in the same answer: if you call the tool before you have said anything, you are asked again for the " +
        "goodbye and the caller waits for it.";

    /// <inheritdoc />
    public override JsonElement JsonSchema { get; } = JsonSerializer.Deserialize(Schema, VoiceConversationToolJsonContext.Default.JsonElement);

    /// <inheritdoc />
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var rootRunId = TraconRunContext.Current?.RootRunId;

        if (rootRunId is not { } run || !registry.TryRequestEnd(run))
        {
            return ValueTask.FromResult<object?>(NotAConversationText);
        }

        // The loop is read here, in the tool's own body: it is ambient state of the loop that runs this call, and it is absent
        // when something other than the loop calls the tool.
        if (FunctionInvokingChatClient.CurrentContext is { } loop && GoodbyeWasSaidBeforeTheCall(loop))
        {
            loop.Terminate = true;
        }

        return ValueTask.FromResult<object?>(EndingText);
    }

    /// <summary>
    /// Whether the response that carried this call already holds words, and nothing is left for the loop to ask the model.
    /// </summary>
    /// <param name="loop">The loop's view of the call.</param>
    /// <returns><see langword="true"/> when the loop can stop without a request that would only repeat a goodbye.</returns>
    /// <remarks>
    /// <para>
    /// The words of a response are part of the turn's answer, and the answer is spoken in full whatever the loop does next, so
    /// a response that holds words has said them. The response is read from the history the loop shows the call, streamed or
    /// not: it ends with the assistant messages of the response, and a provider may return the words and the call as one
    /// message or as two.
    /// </para>
    /// <para>
    /// A call with other calls behind it never stops the loop: the loop drops the calls that follow a call that stops it, and
    /// they would never run.
    /// </para>
    /// </remarks>
    private static bool GoodbyeWasSaidBeforeTheCall(FunctionInvocationContext loop)
    {
        if (loop.FunctionCallIndex < loop.FunctionCount - 1)
        {
            return false;
        }

        var messages = loop.Messages;
        var callId = loop.CallContent?.CallId;
        var carrier = -1;

        for (var index = messages.Count - 1; index >= 0 && carrier < 0; index--)
        {
            if (messages[index].Contents.Any(content => content is FunctionCallContent call && string.Equals(call.CallId, callId, StringComparison.Ordinal)))
            {
                carrier = index;
            }
        }

        if (carrier < 0)
        {
            return false;
        }

        // The response is the run of assistant messages around the one that carries the call; whatever came before that run
        // (a tool's result, the caller's words) belongs to an earlier request.
        var first = carrier;

        while (first > 0 && messages[first - 1].Role == ChatRole.Assistant)
        {
            first--;
        }

        for (var index = first; index < messages.Count && messages[index].Role == ChatRole.Assistant; index++)
        {
            if (!string.IsNullOrWhiteSpace(messages[index].Text))
            {
                return true;
            }
        }

        return false;
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(JsonElement))]
internal sealed partial class VoiceConversationToolJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
