using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// An immutable snapshot of a single tenant's discovered MCP tools.
/// </summary>
internal sealed class McpTenantTools
{
    private readonly Dictionary<string, AIFunction> _tools;

    private McpTenantTools(Dictionary<string, AIFunction> tools, IReadOnlyList<ToolDescriptor> descriptors)
    {
        _tools = tools;
        Descriptors = descriptors;
    }

    /// <summary>The empty set.</summary>
    public static McpTenantTools Empty { get; } = new([], []);

    /// <summary>The tool descriptors shown in the UI; sorted by name.</summary>
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }

    /// <summary>Gets the tool with the given name.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="tool">The tool found.</param>
    /// <returns><see langword="true"/> if the tool is registered.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool)
        => _tools.TryGetValue(name, out tool);

    /// <summary>Builds an immutable set from registrations.</summary>
    /// <param name="registrations">The discovered tool registrations.</param>
    /// <param name="logger">The logger name collisions are reported to.</param>
    /// <returns>The set.</returns>
    /// <remarks>
    /// A name collision is <strong>not an error</strong>. In the code
    /// registry a collision is caught at compile time, and throwing there is
    /// correct; in MCP the name comes from a remote server, and two servers
    /// producing the same name must not crash AgentPrism. The second
    /// registration is skipped and a warning is logged.
    /// </remarks>
    public static McpTenantTools Create(
        IReadOnlyList<AgentPrismToolRegistration> registrations,
        ILogger logger)
    {
        var tools = new Dictionary<string, AIFunction>(registrations.Count, StringComparer.Ordinal);
        var descriptors = new List<ToolDescriptor>(registrations.Count);

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // The approval wrapping happens here. For tools registered in
            // code, ToolRegistry does the same job; MCP tools do not go
            // through that registry, so the wrapping is repeated on this path.
            var function = registration.RequiresApproval
                ? new ApprovalRequiredAIFunction(registration.Function)
                : registration.Function;

            if (!tools.TryAdd(name, function))
            {
                logger.LogWarning(
                    "MCP tool name '{ToolName}' was produced more than once; the second registration was skipped. " +
                    "Choose server names that are distinguishable from one another.",
                    name);

                continue;
            }

            descriptors.Add(new ToolDescriptor
            {
                Name = name,
                Description = registration.Function.Description,
                JsonSchema = registration.Function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : registration.Function.JsonSchema.GetRawText(),
                RequiresApproval = registration.RequiresApproval,
                Source = registration.Source,
            });
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return new McpTenantTools(tools, descriptors);
    }
}
