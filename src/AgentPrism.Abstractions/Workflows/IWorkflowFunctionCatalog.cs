namespace AgentPrism;

/// <summary>
/// The registry of workflow function nodes registered in code.
/// </summary>
/// <remarks>
/// This registry is one of AgentPrism's <strong>security boundaries</strong>,
/// the same one <see cref="IToolRegistry"/> draws for tools. A workflow
/// definition can only point to a registered function by name; the function's
/// <em>code</em> cannot be written from the UI, only selected from this list.
/// </remarks>
public interface IWorkflowFunctionCatalog
{
    /// <summary>Returns the descriptors of all registered functions, ordered by name.</summary>
    /// <returns>The function descriptors.</returns>
    IReadOnlyList<WorkflowFunctionDescriptor> List();

    /// <summary>Returns whether a function with the given name is registered.</summary>
    /// <param name="name">The function name. Comparison is case-sensitive.</param>
    /// <returns><see langword="true"/> if the function is registered.</returns>
    bool Contains(string name);
}
