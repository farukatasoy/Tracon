namespace Tracon;

/// <summary>The agent extension step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>
    /// Reports every extension name of the definition that is not registered or is listed
    /// twice, each with the path of its list item.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// The rules are the compiler's own (<see cref="AgentExtensionRegistry.FindProblems"/>):
    /// a report that applied a copy of them could pass a definition the compiler then
    /// refuses. No extension runs here; the compilation step that follows runs the ones
    /// the definition names, so an extension that throws or changes a protected member
    /// reports as <c>compilation_error</c>.
    /// </remarks>
    private void CheckExtensions(AgentDefinition definition, List<ValidationMessage> messages)
    {
        foreach (var problem in _compiler.GetAgentExtensions().FindProblems(definition.ExtensionNames))
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = problem.Code,
                Message = problem.Message,
                Path = problem.Path,
            });
        }
    }
}
