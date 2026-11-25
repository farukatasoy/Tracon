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
/// The tool is registered by <c>UseVoiceConversation</c> and the agent has to name it in its <c>ToolNames</c>; an agent that
/// does not can never end a conversation. It takes no argument and ends nothing outside a voice conversation.
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
        "(the topic is done, the time is up, or the caller said goodbye) and say a short goodbye in the same answer.";

    /// <inheritdoc />
    public override JsonElement JsonSchema { get; } = JsonSerializer.Deserialize(Schema, VoiceConversationToolJsonContext.Default.JsonElement);

    /// <inheritdoc />
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var rootRunId = TraconRunContext.Current?.RootRunId;

        return ValueTask.FromResult<object?>(
            rootRunId is { } run && registry.TryRequestEnd(run) ? EndingText : NotAConversationText);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(JsonElement))]
internal sealed partial class VoiceConversationToolJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
