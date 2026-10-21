using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The agent an <see cref="IAgentExtension"/> is configuring: the definition, the
/// tenant, the one options object the compiler is about to build the agent from, and
/// the wrappers and chat client rings the extension adds.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="ChatAgentOptions"/> and <see cref="HarnessAgentOptions"/>
/// is set. It is the one the agent's kind selects: a definition with
/// <see cref="AgentDefinition.Harness"/> is a harness agent. Change the options
/// object in place; do not keep it. The compiler reads it once after the last
/// extension has run.
/// </remarks>
public sealed class AgentExtensionContext
{
    private readonly List<Func<AIAgent, AIAgent>> _wrappers = [];
    private readonly List<(ChatClientSlot Slot, Func<IChatClient, IChatClient> Ring)> _rings = [];

    internal AgentExtensionContext(
        AgentDefinition definition,
        string tenantId,
        IServiceProvider services,
        ChatClientAgentOptions? chatAgentOptions,
        HarnessAgentOptions? harnessAgentOptions)
    {
        Definition = definition;
        TenantId = tenantId;
        Services = services;
        ChatAgentOptions = chatAgentOptions;
        HarnessAgentOptions = harnessAgentOptions;
    }

    /// <summary>Gets the definition the agent is compiled from. Changing a copy of it has no effect.</summary>
    public AgentDefinition Definition { get; }

    /// <summary>Gets the tenant the agent is compiled for.</summary>
    public string TenantId { get; }

    /// <summary>
    /// Gets the application's root service provider. A scoped service is not available
    /// here: the compiled agent outlives the request that compiled it.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the options of a chat agent, or <see langword="null"/> when the agent is a
    /// harness agent.
    /// </summary>
    public ChatClientAgentOptions? ChatAgentOptions { get; }

    /// <summary>
    /// Gets the options of a harness agent, or <see langword="null"/> when the agent is a
    /// chat agent.
    /// </summary>
    // MAAI001: Microsoft.Agents.AI.HarnessAgentOptions members are marked "evaluation
    // purposes only". Naming the type here does not need the suppression; using its
    // members in the caller's own code does. Rationale: docs/KARARLAR.md, decision K-020.
    public HarnessAgentOptions? HarnessAgentOptions { get; }

    /// <summary>
    /// Adds a wrapper that is put around the compiled agent. A Microsoft Agent
    /// Framework middleware (<c>AIAgentBuilder.Use</c>) belongs here.
    /// </summary>
    /// <param name="wrap">Receives the agent and returns the agent that replaces it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wrap"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A wrapper sits innermost: Tracon's own wrappers and the catalog's decorators (run
    /// recording, budget, tool approval) stay outside it. With several wrappers the
    /// first one added is the innermost.
    /// </para>
    /// <para>
    /// A wrapper is not checked. It sees what the model call sees and can change it;
    /// that is what a middleware is for.
    /// </para>
    /// </remarks>
    public void Use(Func<AIAgent, AIAgent> wrap)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        _wrappers.Add(wrap);
    }

    /// <summary>
    /// Adds a ring to the agent's model pipeline in the given slot. A
    /// <c>Microsoft.Extensions.AI</c> <see cref="DelegatingChatClient"/> belongs here, for
    /// example a client that the framework documents as one that sees every model call.
    /// </summary>
    /// <param name="slot">Where the ring sits in the pipeline.</param>
    /// <param name="ring">Receives the client below the ring and returns the ring.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ring"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a defined value.</exception>
    /// <remarks>
    /// <para>
    /// The agent keeps every Tracon ring around the one you add: the content guard, the run
    /// budget, the response cache and the <c>chat</c> span stay below it, and the circuit breaker,
    /// the fallback chain and error normalization stay above it. A fallback link that answers in
    /// place of the agent's model gets the same ring. The compaction summarizer and the judge of
    /// a loop criterion do not.
    /// </para>
    /// <para>
    /// With several rings in one slot the first one added is the innermost, as with
    /// <see cref="Use"/>. A ring is not checked. It sees what the model call sees and can
    /// change it. The content guard still inspects the user, assistant and tool messages that it
    /// sends. The guard does not inspect a system message or <c>ChatOptions.Instructions</c>, and
    /// the ring receives the response after the guard has inspected it.
    /// </para>
    /// <para>
    /// <paramref name="ring"/> runs each time the pipeline is built: when the agent is compiled,
    /// and when a fallback link is first used. Keep it cheap and keep state that belongs to one
    /// run out of it. Derive the ring from <see cref="DelegatingChatClient"/>: a
    /// <see cref="ChatClientSlot.Turn"/> ring that hides the tool-call loop below it from
    /// <see cref="IChatClient.GetService"/> fails the compilation.
    /// </para>
    /// <para>
    /// An exception that the ring throws while it serves a call is the host's own. It is not
    /// counted by the circuit breaker and not masked as a provider failure, as with a wrapper
    /// added by <see cref="Use"/>. A provider failure that passes up through the ring is still
    /// both.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentExtension(
    ///            "model-call-log",
    ///            context => context.UseChatClient(
    ///                ChatClientSlot.ModelCall,
    ///                inner => new ModelCallLogChatClient(inner)));
    /// </code>
    /// </example>
    /// </remarks>
    public void UseChatClient(ChatClientSlot slot, Func<IChatClient, IChatClient> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        if (slot is not (ChatClientSlot.ModelCall or ChatClientSlot.Turn))
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"{slot} is not a {nameof(ChatClientSlot)} value.");
        }

        _rings.Add((slot, ring));
    }

    internal IReadOnlyList<Func<AIAgent, AIAgent>> Wrappers => _wrappers;

    internal IReadOnlyList<(ChatClientSlot Slot, Func<IChatClient, IChatClient> Ring)> Rings => _rings;
}
