namespace Tracon;

/// <summary>
/// What the factory of a hosted tool (<c>AddHostedTool</c>) learns about the agent it builds the
/// tool for.
/// </summary>
/// <remarks>
/// <para>
/// The factory runs on every run of an agent that selects the tool. Tracon calls it to compare what it
/// builds with the compiled agent in its cache, and compiles the agent again only when the two differ
/// (a new tenant, a new definition, a replaced vector store). Keep it cheap and free of side effects:
/// build the tool from values you already hold, and do not call the network.
/// </para>
/// <para>
/// A value that belongs to the tenant, for instance the identifier of the tenant's own vector
/// store, is read here by <see cref="TenantId"/>. An identifier is not a <c>secret</c>; never put
/// a credential into a hosted tool.
/// </para>
/// </remarks>
public sealed class HostedToolContext
{
    /// <summary>Gets the tenant the agent is compiled for.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the name of the model provider of the agent's primary binding, for example
    /// <c>openai-responses</c>.
    /// </summary>
    /// <remarks>
    /// A fallback link receives the same tool, so a factory that returns a different tool for each
    /// provider serves the primary binding only.
    /// </remarks>
    public required string Provider { get; init; }

    /// <summary>Gets the host's root service provider.</summary>
    public required IServiceProvider Services { get; init; }
}
