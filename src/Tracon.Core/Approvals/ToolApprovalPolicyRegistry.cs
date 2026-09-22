using System.Diagnostics.CodeAnalysis;

namespace Tracon;

/// <summary>Holds the code-defined approval policies registered at startup, keyed by tool name.</summary>
/// <remarks>
/// One tool has at most one policy: a second registration for the same tool name is
/// a configuration mistake, not a "last one wins" situation — a security policy the
/// consumer expected to be active must not be silently dropped.
/// </remarks>
internal sealed class ToolApprovalPolicyRegistry
{
    private readonly Dictionary<string, Func<ToolApprovalContext, ToolApprovalPolicyDecision>> _policies;

    /// <summary>Creates a new registry from registrations.</summary>
    /// <param name="registrations">The policy registrations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same tool name is registered more than once.</exception>
    public ToolApprovalPolicyRegistry(IEnumerable<ToolApprovalPolicyRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _policies = new Dictionary<string, Func<ToolApprovalContext, ToolApprovalPolicyDecision>>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            if (!_policies.TryAdd(registration.ToolName, registration.Policy))
            {
                throw new TraconException(
                    $"More than one approval policy is registered for tool '{registration.ToolName}'. " +
                    "A tool can have only one code-defined policy.");
            }
        }
    }

    /// <summary>Looks up the policy for a tool, if one is registered.</summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="policy">The policy delegate, when found.</param>
    public bool TryGet(string toolName, [NotNullWhen(true)] out Func<ToolApprovalContext, ToolApprovalPolicyDecision>? policy)
        => _policies.TryGetValue(toolName, out policy);
}
