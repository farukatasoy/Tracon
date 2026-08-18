using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// The tool registry created from <see cref="AgentPrismToolRegistration"/> entries.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, AIFunctionDeclaration> _tools;
    private readonly List<ToolDescriptor> _descriptors;

    /// <summary>Initializes a new registry from registrations.</summary>
    /// <param name="registrations">The tool registrations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// The same name is registered more than once, or a client-side tool
    /// (one whose body is not an <see cref="AIFunction"/>) is registered
    /// with <c>requiresApproval: true</c>.
    /// </exception>
    public ToolRegistry(IEnumerable<AgentPrismToolRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _tools = new Dictionary<string, AIFunctionDeclaration>(StringComparer.Ordinal);
        _descriptors = [];

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // Apply the approval wrapper here, not in the compiler. The registry is
            // the only place that enforces the "an agent can only refer to a registered
            // tool" rule. Enforcing approval here prevents another code path from bypassing it.
            //
            // ApprovalRequiredAIFunction is a DelegatingAIFunction. Its name,
            // description, and JSON schema do not change. Instead of running the
            // wrapped tool, Microsoft Agent Framework produces ToolApprovalRequestContent.
            AIFunctionDeclaration function;

            if (registration.RequiresApproval)
            {
                if (registration.Function is not AIFunction invocable)
                {
                    throw new AgentPrismException(
                        $"Tool '{name}' cannot require approval: it runs on the client and has no " +
                        "server-side body to defer. Approval and client-side tools are separate mechanisms.");
                }

                function = new ApprovalRequiredAIFunction(invocable);
            }
            else
            {
                function = registration.Function;
            }

            if (!_tools.TryAdd(name, function))
            {
                throw new AgentPrismException(
                    $"More than one tool is registered with name '{name}'. Tool names must be unique.");
            }

            _descriptors.Add(new ToolDescriptor
            {
                Name = name,
                Description = registration.Function.Description,
                JsonSchema = registration.Function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : registration.Function.JsonSchema.GetRawText(),
                RequiresApproval = registration.RequiresApproval,
                Source = registration.Source,
                RunsOnClient = registration.Function is not AIFunction,
            });
        }

        _descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> List() => _descriptors;

    /// <inheritdoc />
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryGetValue(name, out tool);
    }
}
