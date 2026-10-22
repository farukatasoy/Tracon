namespace Tracon;

/// <summary>
/// Settings for the session workspace of an agent: a file area that belongs to one
/// conversation, where the agent lists, reads, searches, writes, edits and deletes files.
/// </summary>
/// <remarks>
/// <para>
/// The workspace is private to one session of one agent. Another session does not see its
/// files, and it is not the agent's file memory (<c>MemorySettings.EnableFileMemory</c>): file
/// memory lives as long as the agent, a workspace lives as long as its session. When the session
/// goes, whichever way (<c>DELETE /api/sessions/{id}</c>, an erasure of its data subject, or its
/// retention period), its files go with it. A run that has no session has no workspace and gets no
/// file tools.
/// </para>
/// <para>
/// Microsoft Agent Framework supplies the eight <c>file_access_*</c> tools. Tracon runs them
/// through the same tool chain as a tool registered in code: the authorization handler, argument
/// validation, the timeout, and the approval step. Every tool that changes a file asks for
/// approval, always; there is deliberately no member that turns that off. The size of a file, of
/// one session's workspace and of one tenant's workspaces is limited by <c>Tracon:Workspace</c>
/// (<c>TraconWorkspaceOptions</c>).
/// </para>
/// <para>
/// An agent that is exposed over A2A or MCP has nobody to answer an approval request. The host
/// refuses to expose a workspace that can change a file or that asks for approval to read
/// one: set <see cref="ReadOnly"/> and <see cref="DisableReadApproval"/> together, or leave the
/// agent off the allow list.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// var definition = new AgentDefinition
/// {
///     Name = "drafter",
///     Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" },
///     Workspace = new WorkspaceSettings { Enabled = true },
/// };
/// </code>
/// </example>
public sealed record WorkspaceSettings
{
    /// <summary>
    /// Gets a value that gives the agent a workspace in every session that has a record.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets a value that offers only the tools that read: <c>file_access_read</c>,
    /// <c>file_access_read_lines</c>, <c>file_access_ls</c> and <c>file_access_grep</c>.
    /// The tools that write, replace and delete are not offered at all.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets a value that makes the tools that read run without asking for approval. The tools
    /// that change a file keep their approval.
    /// </summary>
    /// <remarks>
    /// What the agent wrote earlier in the session reaches the model again when it reads. Turn
    /// this on when nobody should be asked about a read; the content is then a prompt-injection
    /// surface that no person reviews at run time.
    /// </remarks>
    public bool DisableReadApproval { get; init; }
}
