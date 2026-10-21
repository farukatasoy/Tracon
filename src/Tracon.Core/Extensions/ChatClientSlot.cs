namespace Tracon;

/// <summary>
/// Where a chat client ring that an agent extension adds sits in the agent's model pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Tracon builds the model pipeline of a compiled agent, from the outside in: error
/// normalization, finish-reason detection, the fallback chain, the circuit breaker,
/// attachment resolution, the tool-call loop, the run budget, the response cache, hosted tool
/// usage, the <c>chat</c> span, the content guard, the concurrency limit, and the provider's
/// raw client. A ring goes into one of two fixed places in that order. It never goes below
/// the content guard, so the guard inspects the user, assistant and tool messages that the
/// ring sends.
/// </para>
/// <para>
/// The guard does not inspect a system message or <c>ChatOptions.Instructions</c>, on any
/// agent, and a ring receives the response after the guard has inspected it. Do not let a
/// ring put text it did not write into a system message or into the instructions.
/// </para>
/// <para>
/// Add a ring with <see cref="AgentExtensionContext.UseChatClient"/>.
/// </para>
/// </remarks>
public enum ChatClientSlot
{
    /// <summary>
    /// Inside the tool-call loop, above the run budget, the response cache, the <c>chat</c> span
    /// and the content guard. The ring sees every model call of a turn, including the call that
    /// takes a tool result to the model. A user or tool message that the ring adds reaches the
    /// content guard, the budget counts the request that the ring sends, and the cache key is
    /// built from that request.
    /// </summary>
    ModelCall,

    /// <summary>
    /// Outside the tool-call loop, below attachment resolution, the circuit breaker and the
    /// fallback chain. The ring sees one call for each agent turn, and the loop runs inside that
    /// call. A turn that the fallback chain tries again on another model goes through the same
    /// ring of that model.
    /// </summary>
    Turn,
}
