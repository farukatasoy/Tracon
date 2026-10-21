using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The chat client rings the extensions of one agent added, in the order they were added,
/// and the one place that puts them into a model pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelProviderRegistry</c> calls <see cref="Wrap"/> for each slot while it builds the
/// agent's own model client and every fallback link of it, so a link that answers in place of
/// the primary model runs the same rings. The compaction summarizer and the loop judge get no
/// ring: they serve their own call, not the agent's turn.
/// </para>
/// <para>
/// A ring factory runs each time a pipeline is built: when the agent is compiled, and when a
/// fallback link is first used. A failure names the extension and the exception type only;
/// the message of a host's exception can carry a connection string.
/// </para>
/// </remarks>
internal sealed class ChatClientRings
{
    private readonly IReadOnlyList<ExtensionChatClientRing> _modelCall;
    private readonly IReadOnlyList<ExtensionChatClientRing> _turn;

    /// <summary>Creates the rings of one agent.</summary>
    /// <param name="agentName">The agent's name, for the error.</param>
    /// <param name="modelCall">The <see cref="ChatClientSlot.ModelCall"/> rings, the first one innermost.</param>
    /// <param name="turn">The <see cref="ChatClientSlot.Turn"/> rings, the first one innermost.</param>
    public ChatClientRings(
        string agentName,
        IReadOnlyList<ExtensionChatClientRing> modelCall,
        IReadOnlyList<ExtensionChatClientRing> turn)
    {
        AgentName = agentName;
        _modelCall = modelCall;
        _turn = turn;
    }

    /// <summary>Gets the rings of an agent whose extensions added none.</summary>
    public static ChatClientRings Empty { get; } = new(string.Empty, [], []);

    /// <summary>Gets the name of the agent the rings belong to.</summary>
    public string AgentName { get; }

    /// <summary>Gets a value indicating whether no extension added a ring.</summary>
    public bool IsEmpty => _modelCall.Count == 0 && _turn.Count == 0;

    /// <summary>Gets the names of the extensions that added a ring, each once, in the order they added the first one.</summary>
    public IReadOnlyList<string> Extensions
        => [.. _modelCall.Concat(_turn).Select(static ring => ring.Extension).Distinct(StringComparer.Ordinal)];

    /// <summary>Puts the rings of one slot around a client, the first one innermost.</summary>
    /// <param name="slot">The slot whose rings are applied.</param>
    /// <param name="client">The client the slot's innermost ring receives.</param>
    /// <returns>The outermost ring; <paramref name="client"/> itself when the slot has none.</returns>
    /// <exception cref="TraconCompilationException">
    /// A ring factory threw or returned <see langword="null"/>, or a <see cref="ChatClientSlot.Turn"/>
    /// ring hides the tool-call loop below it.
    /// </exception>
    public IChatClient Wrap(ChatClientSlot slot, IChatClient client)
    {
        foreach (var (extension, ring) in slot == ChatClientSlot.ModelCall ? _modelCall : _turn)
        {
            try
            {
                client = ring(client) ?? throw new TraconCompilationException(
                    $"Agent extension '{extension}' returned no chat client from a ring while the model pipeline of agent '{AgentName}' was built.")
                {
                    AgentName = AgentName,
                };
            }
            catch (TraconCompilationException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new TraconCompilationException(
                    $"Agent extension '{extension}' failed in a chat client ring while the model pipeline of agent '{AgentName}' was built " +
                    $"({exception.GetType().Name}).",
                    exception)
                {
                    AgentName = AgentName,
                };
            }

            // Microsoft Agent Framework adds its own tool-call loop above a chat client in which
            // it finds none, and Tracon reads the loop's settings through the same lookup. A Turn
            // ring sits above Tracon's loop, so it must keep the loop visible.
            if (slot == ChatClientSlot.Turn && client.GetService(typeof(FunctionInvokingChatClient)) is null)
            {
                throw new TraconCompilationException(
                    $"Agent extension '{extension}' added a {nameof(ChatClientSlot.Turn)} ring that hides Tracon's tool-call loop " +
                    $"while the model pipeline of agent '{AgentName}' was built. Derive the ring from {nameof(DelegatingChatClient)}, " +
                    $"or pass {nameof(IChatClient.GetService)} calls that it does not answer to the client it wraps.")
                {
                    AgentName = AgentName,
                };
            }
        }

        return client;
    }
}

/// <summary>A chat client ring an extension added, with the extension's name for the error message.</summary>
/// <param name="Extension">The extension's name.</param>
/// <param name="Ring">Receives the client below the ring and returns the ring.</param>
internal readonly record struct ExtensionChatClientRing(string Extension, Func<IChatClient, IChatClient> Ring);
