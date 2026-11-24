using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates code-defined tool registrations while the host starts.</summary>
internal sealed class ToolRegistrationValidationService(
    IToolRegistry registry,
    IEnumerable<TraconToolRegistration> registrations,
    IOptions<TraconOptions> options,
    ILogger<ToolRegistrationValidationService> logger,
    HostedToolRegistry? hostedTools = null,
    ToolApprovalPolicyRegistry? approvalPolicies = null) : ITraconStartupCheck
{
    /// <inheritdoc />
    public StartupCheckKind Kind => StartupCheckKind.Gate;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (registry is not IVerifiedToolRegistry)
        {
            if (!options.Value.Tools.AllowUnverifiedToolRegistry)
            {
                throw new TraconException("IToolRegistry was replaced. This removes the Authorizing, Timeout, ApprovalRequired, and Truncating wrappers. Use AddTool APIs, or explicitly set Tools.AllowUnverifiedToolRegistry to true.");
            }

            logger.LogWarning("An unverified IToolRegistry is active. Authorizing, Timeout, ApprovalRequired, and Truncating wrappers are not guaranteed.");
        }

        // One name never means two tools: a definition selects a hosted tool and a function tool by the
        // same list, and the compiler would have to pick one.
        foreach (var descriptor in hostedTools?.Descriptors ?? [])
        {
            if (registry.TryGet(descriptor.Name, out _))
            {
                throw new TraconException(
                    $"The name '{descriptor.Name}' is registered as a hosted tool and as a function tool. Tool names must be unique.");
            }
        }

        CheckApprovalPolicies();

        foreach (var registration in registrations)
        {
            if (registration.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            {
                throw new TraconException($"Tool '{registration.Function.Name}' has a timeout that must be greater than zero.");
            }

            if (registration.MaxOutputBytes is { } maxOutputBytes && maxOutputBytes < TruncatingAIFunction.MinimumEnvelopeBytes)
            {
                throw new TraconException($"Tool '{registration.Function.Name}' has MaxOutputBytes {maxOutputBytes}, which is below {TruncatingAIFunction.MinimumEnvelopeBytes}.");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// A policy holds a call back only where Tracon runs the call. A hosted tool runs on the provider's side: no call reaches
    /// Tracon, so there is nothing to hold, and a policy named for one would read as a gate that is not there.
    /// </summary>
    /// <remarks>
    /// A name that no registered tool carries is not an error. A tool of an MCP server is added per tenant after the host has
    /// started, and the tools a run brings with it are not in the registry; a policy for either applies when the tool does.
    /// The log names those policies so a misspelled name does not pass unseen, and says what a policy cannot hold: only a tool
    /// that runs through the tool chain has a call to hold back, so a client-side tool, the skill tools and an agent called as
    /// a tool are not held by one.
    /// </remarks>
    private void CheckApprovalPolicies()
    {
        if (approvalPolicies is null)
        {
            return;
        }

        var hosted = hostedTools?.Descriptors.Select(static descriptor => descriptor.Name).ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var name in approvalPolicies.Names.OrderBy(static name => name, StringComparer.Ordinal))
        {
            if (hosted.Contains(name))
            {
                throw new TraconException(
                    $"An approval policy is registered for '{name}', which is a hosted tool. A hosted tool runs on the " +
                    "provider's side and no call reaches Tracon, so the policy could never hold one back. Remove the policy.");
            }

            if (!registry.TryGet(name, out _) && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "An approval policy is registered for '{ToolName}', and no tool registered in code carries that name. It holds a tool of that name that an MCP server or a run brings with it. It holds nothing for a tool that does not run through Tracon's tool chain: a client-side tool, a skill tool or an agent called as a tool.",
                    name);
            }
        }
    }
}
