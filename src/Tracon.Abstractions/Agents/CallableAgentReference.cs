namespace Tracon;

/// <summary>
/// One agent another agent may call, and the version of it that runs.
/// </summary>
/// <remarks>
/// <para>
/// Without a <see cref="Version"/> the callee runs its <strong>current</strong> version: an edit of the callee
/// reaches the caller at the next call. With one, the callee runs that stored version, and for an agent kept in the
/// database its description (the text the model reads to decide when to call it) is read from that version too.
/// A callee from a custom agent source keeps its current description; the source decides whether it has the
/// version. A caller pinned to a version of its own (a workflow step, an approval resumption) runs the callee
/// versions its <em>own</em> definition names, not the ones the callee has now.
/// </para>
/// <para>
/// Only an agent kept in the database has versions. A pin on an agent defined in code is refused when the
/// definition is saved and when it is compiled.
/// </para>
/// <para>
/// A name converts to a reference without a version, so a list of names stays a list of names:
/// <c>CallableAgents = ["researcher"]</c>.
/// </para>
/// </remarks>
/// <example>
/// Calls <c>researcher</c> as it was at version 3 and <c>editor</c> as it is now:
/// <code language="csharp">
/// new AgentDefinition
/// {
///     Name = "writer",
///     Model = new ModelBinding { Provider = "openai", Model = "gpt-5" },
///     CallableAgents = [new CallableAgentReference { Name = "researcher", Version = 3 }, "editor"],
/// };
/// </code>
/// </example>
public sealed record CallableAgentReference
{
    /// <summary>Gets the name of the agent to call.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the version of the agent to run. <see langword="null"/> runs its current version.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>Makes a reference to the current version of the agent with the given name.</summary>
    /// <param name="name">The agent name.</param>
    /// <returns>A reference with no version.</returns>
    public static CallableAgentReference FromName(string name)
        => new() { Name = name };

    /// <summary>Makes a reference to the current version of the agent with the given name.</summary>
    /// <param name="name">The agent name.</param>
    /// <returns>A reference with no version.</returns>
    /// <remarks>Same as <see cref="FromName(string)"/>.</remarks>
    public static implicit operator CallableAgentReference(string name)
        => FromName(name);
}
