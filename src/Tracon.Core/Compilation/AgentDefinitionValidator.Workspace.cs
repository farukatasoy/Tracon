namespace Tracon;

/// <summary>The workspace settings step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The code of the warning about workspace settings on an agent that has no workspace.</summary>
    internal const string WorkspaceSettingsWithoutWorkspaceCode = ValidationCodes.WorkspaceSettingsWithoutWorkspace;

    /// <summary>The code of the error about an agent name the workspace paths cannot hold.</summary>
    internal const string WorkspaceAgentNameCode = ValidationCodes.WorkspaceAgentName;

    /// <summary>
    /// Checks a definition that carries <see cref="AgentDefinition.Workspace"/>: settings that change nothing
    /// because the workspace is off, and a name the workspace cannot use.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// The first is a warning, not an error: a form that keeps the settings while the author turns the
    /// workspace off is not a mistake the compiler needs to stop. The second is an error, the same one the
    /// compiler throws, so the report names it before the structure check does.
    /// </remarks>
    private static void CheckWorkspace(AgentDefinition definition, List<ValidationMessage> messages)
    {
        if (definition.Workspace is not { } workspace)
        {
            return;
        }

        if (!workspace.Enabled)
        {
            if (workspace.ReadOnly || workspace.DisableReadApproval)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = WorkspaceSettingsWithoutWorkspaceCode,
                    Message = $"Agent '{definition.Name}' sets {nameof(AgentDefinition.Workspace)}, but {nameof(WorkspaceSettings.Enabled)} " +
                              "is off, so the settings change nothing.",
                    Path = "workspace",
                });
            }

            return;
        }

        if (definition.Name.Contains('/', StringComparison.Ordinal))
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = WorkspaceAgentNameCode,
                Message = $"Agent '{definition.Name}' has a session workspace, but its name holds a '/'. " +
                          "The name is a segment of the path of the workspace.",
                Path = "name",
            });
        }
    }
}
