namespace Tracon;

/// <summary>
/// An agent source that can describe one agent by its name without listing every agent it holds.
/// </summary>
/// <remarks>
/// <para>
/// Internal on purpose, like <see cref="IDefinitionBackedSource"/>: the database source and the adapter of
/// <see cref="IAgentDefinitionSource"/> implement it. The descriptor they return is the one their own listing returns for
/// that name, so the catalog can answer "what does this one agent look like" with one read instead of a listing of the
/// whole tenant. A source that does not implement it is listed, as before.
/// </para>
/// <para>
/// A caller that wants only the description and the version of the agents a definition calls reads them this way: the
/// listing of 5 000 agents is about 8 MB on the wire, and it was read on every resolve of a calling agent.
/// </para>
/// </remarks>
internal interface INamedDescriptorSource
{
    /// <summary>Describes the current version of the agent with the given name.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The descriptor its listing carries; <see langword="null"/> when this source does not hold the name.</returns>
    ValueTask<AgentDescriptor?> DescribeAsync(string agentName, CancellationToken cancellationToken);
}
