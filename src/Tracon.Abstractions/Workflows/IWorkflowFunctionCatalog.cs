using System.Text.Json;

namespace Tracon;

/// <summary>
/// The registry of workflow function nodes registered in code.
/// </summary>
/// <remarks>
/// This registry is one of Tracon's <strong>security boundaries</strong>,
/// the same one <see cref="IToolRegistry"/> draws for tools. A workflow
/// definition can only point to a registered function by name; the function's
/// <em>code</em> cannot be written from the UI, only selected from this list.
/// </remarks>
internal interface IWorkflowFunctionCatalog
{
    /// <summary>Returns the descriptors of all registered functions, ordered by name.</summary>
    /// <returns>The function descriptors.</returns>
    IReadOnlyList<WorkflowFunctionDescriptor> List();

    /// <summary>Returns whether a function with the given name is registered.</summary>
    /// <param name="name">The function name. Comparison is case-sensitive.</param>
    /// <returns><see langword="true"/> if the function is registered.</returns>
    bool Contains(string name);

    /// <summary>
    /// Checks the settings of a function node against the settings type the
    /// function declares.
    /// </summary>
    /// <param name="name">The function name. Comparison is case-sensitive.</param>
    /// <param name="settings">
    /// The node's <see cref="WorkflowNodeReference.Settings"/>, or <see langword="null"/>
    /// when the node carries none.
    /// </param>
    /// <returns>
    /// A user-facing message that names the function and the problem, or
    /// <see langword="null"/> when the settings are valid. A function that declares
    /// no settings type is valid only with <see langword="null"/>. A function that
    /// declares one is checked by reading the object into it: an unknown field, a
    /// missing required member, a value of the wrong type and a <c>null</c> in a
    /// member that does not allow it are all refused. An absent object is read as
    /// <c>{}</c>, so a type with a required member refuses a node that has no
    /// settings. Settings whose JSON text is larger than the limit the workflow
    /// engine is configured with (<c>MaxNodeSettingsLength</c>) are refused too,
    /// before they are read.
    /// </returns>
    /// <remarks>
    /// This is the save-time check (<c>PUT /api/workflows/{name}</c>): the function
    /// registry is fixed for the lifetime of the process, so checking early is an
    /// honest guarantee, unlike an agent name. The compiler checks again when a
    /// run starts, because the settings type can change between deployments.
    /// </remarks>
    string? ValidateSettings(string name, JsonElement? settings);
}
