using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Session workspace provider production.
/// </summary>
// MAAI001: Microsoft.Agents.AI.AgentFileStore is marked "evaluation purposes only". Workspace setup is kept
// in this file and in Workspace/; only these are updated if MAF changes these APIs. Rationale:
#pragma warning disable MAAI001
public sealed partial class AgentDefinitionCompiler
{
    /// <summary>
    /// Builds the provider that gives the agent the file tools of the session it runs in
    /// (<see cref="WorkspaceSettings.Enabled"/>).
    /// </summary>
    /// <returns>The provider, or <see langword="null"/> when the definition has no workspace.</returns>
    /// <exception cref="TraconCompilationException">
    /// The agent's name holds a <c>/</c>, no file store is registered, or the services the workspace needs
    /// are not registered.
    /// </exception>
    private SessionWorkspaceProvider? CreateWorkspaceProvider(AgentDefinition definition)
    {
        if (definition.Workspace is not { Enabled: true } workspace)
        {
            return null;
        }

        // The agent's name is one segment of the path of its files. A "/" in it would make "a/b" with
        // the file "c" and "a" with the file "b/c" the same file.
        if (definition.Name.Contains('/', StringComparison.Ordinal))
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' wants a session workspace, but its name holds a '/'. " +
                "The name is a segment of the path of the workspace; rename the agent.")
            {
                AgentName = definition.Name,
            };
        }

        if (_fileStore is null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' wants a session workspace, but the file store is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        if (_services?.GetService(typeof(BuiltInToolGovernance)) is not BuiltInToolGovernance governance
            || _services.GetService(typeof(IOptionsMonitor<TraconWorkspaceOptions>)) is not IOptionsMonitor<TraconWorkspaceOptions> limits)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' wants a session workspace, but the services it needs are not registered. " +
                "Register Tracon with AddTracon.")
            {
                AgentName = definition.Name,
            };
        }

        if (_fileStore is InMemoryAgentFileStore
            && _tenantContext is not null and not (FixedTenantContext or SingleTenantContext)
            && _loggerFactory?.CreateLogger<AgentDefinitionCompiler>() is { } logger
            && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Agent {Agent} has a session workspace, but the file store is the in-memory one: the files are lost when the process restarts. " +
                "Use a SQL provider (UseSqlite from Tracon.Sqlite, UsePostgreSql from Tracon.PostgreSql, UseSqlServer from Tracon.SqlServer) or register your own AgentFileStore.",
                definition.Name);
        }

        var tree = _fileStore as IAgentFileTree ?? new GenericAgentFileTree(_fileStore);
        var store = new SessionWorkspaceFileStore(_fileStore, tree, ResolveTenantId(definition), definition.Name, limits);

        return new SessionWorkspaceProvider(store, workspace, governance);
    }

    /// <summary>
    /// Throws when a tool of the agent has the name of one of the file tools of its session workspace.
    /// </summary>
    /// <exception cref="TraconCompilationException">A tool of the agent is named like a workspace tool.</exception>
    private static void ThrowOnWorkspaceToolConflict(AgentDefinition definition, List<AITool> tools)
    {
        if (definition.Workspace is not { Enabled: true })
        {
            return;
        }

        foreach (var tool in tools)
        {
            if (SessionWorkspaceProvider.ToolNames.Contains(tool.Name, StringComparer.Ordinal))
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' has a tool named '{tool.Name}', which is the name of a file tool of its " +
                    "session workspace. Rename the tool, or turn the workspace off.")
                {
                    AgentName = definition.Name,
                };
            }
        }
    }
}
#pragma warning restore MAAI001
