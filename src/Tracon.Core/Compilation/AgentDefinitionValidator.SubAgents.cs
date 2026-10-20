namespace Tracon;

/// <summary>The sub-agent tool step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>
    /// Reports every callable agent of a <see cref="SubAgentCallMode.Tool"/> mode definition whose
    /// tool name another tool of the agent already has, each with the path of its list item.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// The rule is the compiler's own (<see cref="SubAgentToolNames.FindProblems"/>): a report that
    /// applied a copy of it could pass a definition the compiler then refuses.
    /// </remarks>
    private static void CheckSubAgentTools(AgentDefinition definition, List<ValidationMessage> messages)
    {
        foreach (var problem in SubAgentToolNames.FindProblems(definition))
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
