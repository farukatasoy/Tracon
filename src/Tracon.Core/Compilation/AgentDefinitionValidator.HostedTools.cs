namespace Tracon;

/// <summary>The hosted tool step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>
    /// Reports every hosted tool of the definition that cannot run: a second tool of one kind, a second
    /// web search, a tool mode that requires one, and a provider whose API surface would drop one.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// The rules are the compiler's own (<see cref="HostedToolRegistry.FindProblems"/> and
    /// <see cref="HostedToolSupportCheck"/>): a report that applied a copy of them could pass a definition
    /// the compiler then refuses. No factory runs here; the compilation step that follows builds the tools,
    /// so a factory that throws or returns the wrong type reports as <c>compilation_error</c>.
    /// </remarks>
    private void CheckHostedTools(AgentDefinition definition, List<ValidationMessage> messages)
    {
        var hostedTools = _compiler.GetHostedTools();

        foreach (var problem in hostedTools.FindProblems(definition))
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = problem.Code,
                Message = problem.Message,
                Path = problem.Path,
            });
        }

        foreach (var finding in HostedToolSupportCheck.Unsupported(_models, definition.Model, hostedTools.Selected(definition)))
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "hosted_tool_unsupported",
                Message = finding.Describe(),
                Path = finding.Path,
            });
        }
    }
}
