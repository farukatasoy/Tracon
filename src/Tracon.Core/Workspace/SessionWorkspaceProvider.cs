using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Gives an agent the file tools of the session it is running in, and runs each tool through the Tracon tool chain.
/// </summary>
/// <remarks>
/// <para>
/// Wraps <see cref="FileAccessProvider"/>, which is <see langword="sealed"/>. The wrapper takes the tools the
/// framework provider makes and passes each one to <see cref="WorkspaceToolGovernance"/>, so the model sees
/// only the wrapped tools. The framework's own approval wrapper is off: Tracon sets approval itself, and the
/// tools that change a file always ask (<see cref="WorkspaceSettings.DisableReadApproval"/> reaches the tools that read, and no
/// member reaches the others).
/// </para>
/// <para>
/// A run that has no session, or whose session has no record (a workflow run and everything below it: nothing
/// could delete what it kept), gets no tool and no instruction. The store (<see cref="SessionWorkspaceFileStore"/>)
/// reads the session from the run on every call, so the wrapped tools are built once and shared by every run of the
/// compiled agent.
/// </para>
/// </remarks>
// MAAI001: the context provider contexts and FileAccessProvider are marked "evaluation purposes only" - the
// rationale is the same as in AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001
internal sealed class SessionWorkspaceProvider : AIContextProvider, IDisposable
{
    private readonly FileAccessProvider _inner;
    private readonly SessionWorkspaceFileStore _store;
    private readonly WorkspaceToolGovernance _governance;
    private readonly bool _disableReadApproval;
    private AITool[]? _tools;

    /// <summary>Creates the provider for one compiled agent.</summary>
    /// <param name="store">The store of the workspace.</param>
    /// <param name="settings">The settings of the definition.</param>
    /// <param name="governance">Runs each tool through the tool chain.</param>
    public SessionWorkspaceProvider(SessionWorkspaceFileStore store, WorkspaceSettings settings, WorkspaceToolGovernance governance)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(governance);

        _store = store;
        _governance = governance;
        _disableReadApproval = settings.DisableReadApproval;

        _inner = new FileAccessProvider(
            store,
            new FileAccessProviderOptions
            {
                DisableWriteTools = settings.ReadOnly,

                // Tracon sets approval on the wrapped tool (Govern), in the same place as for every other tool.
                DisableReadOnlyToolApproval = true,
                DisableWriteToolApproval = true,
                Instructions = settings.ReadOnly ? ReadOnlyInstructions : Instructions,
            });
    }

    /// <summary>
    /// The names of the tools the provider offers. An agent cannot have a tool of its own with one of
    /// these names: an approval rule that a person set for the file tool would then cover that tool as well.
    /// </summary>
    internal static IReadOnlyList<string> ToolNames { get; } =
    [
        FileAccessProvider.ReadFileToolName,
        FileAccessProvider.ReadLinesToolName,
        FileAccessProvider.LsToolName,
        FileAccessProvider.GrepToolName,
        FileAccessProvider.WriteToolName,
        FileAccessProvider.DeleteFileToolName,
        FileAccessProvider.ReplaceToolName,
        FileAccessProvider.ReplaceLinesToolName,
    ];

    /// <summary>Gets no state keys: the provider keeps nothing in the session.</summary>
    public override IReadOnlyList<string> StateKeys => [];

    private const string Instructions =
        "## Session workspace\n" +
        "You have a private file workspace for this conversation, through the `file_access_*` tools.\n" +
        "Only this conversation can see its files, and they are deleted when the conversation is deleted.\n" +
        "Use it to keep drafts, notes and results that you need again later in the conversation.\n\n" +
        "- Never delete or overwrite an existing file unless the user has asked you to.\n" +
        "- Files may be organized into subdirectories. Use `file_access_ls` to explore them level by level,\n" +
        "  or `file_access_grep` to search their contents.\n" +
        "- To make a small edit, prefer `file_access_replace` or `file_access_replace_lines` over writing the whole file again.\n" +
        "- A tool that changes a file waits for the user's approval. A write that would pass the size limit of the workspace\n" +
        "  is refused, and the refusal tells you which limit. Delete files you no longer need, or write less.";

    private const string ReadOnlyInstructions =
        "## Session workspace\n" +
        "You can read the files of a private workspace for this conversation, through the `file_access_*` tools.\n" +
        "Only this conversation can see its files. You cannot change them.\n\n" +
        "- Use `file_access_ls` to explore the files level by level, or `file_access_grep` to search their contents.";

    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        if (!_store.HasWorkspace)
        {
            return new AIContext();
        }

        // The framework provider adds its tools to the context it is given, so it gets an empty one and the
        // base class merges the result into the real one.
        var provided = await _inner
            .InvokingAsync(new InvokingContext(context.Agent, session: null, new AIContext()), cancellationToken)
            .ConfigureAwait(false);

        return new AIContext
        {
            Instructions = provided.Instructions,
            Tools = _tools ??= (provided.Tools ?? []).Select(Govern).ToArray(),
        };
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();

    private AITool Govern(AITool tool)
    {
        if (tool is not AIFunction function)
        {
            return tool;
        }

        var effect = EffectOf(function.Name);

        return _governance.Govern(function, effect, requiresApproval: effect != ToolEffect.Read || !_disableReadApproval);
    }

    // A tool this code does not know (a future framework version adds one) is treated as one that changes a file:
    // it asks for approval whatever DisableReadApproval says.
    internal static ToolEffect EffectOf(string toolName) => toolName switch
    {
        FileAccessProvider.ReadFileToolName or FileAccessProvider.ReadLinesToolName or FileAccessProvider.LsToolName or FileAccessProvider.GrepToolName => ToolEffect.Read,
        FileAccessProvider.DeleteFileToolName => ToolEffect.Destructive,
        _ => ToolEffect.Write,
    };
}
#pragma warning restore MAAI001
