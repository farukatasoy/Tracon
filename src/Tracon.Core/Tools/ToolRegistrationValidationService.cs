using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Validates code-defined tool registrations while the host starts.</summary>
internal sealed class ToolRegistrationValidationService(
    IToolRegistry registry,
    IEnumerable<TraconToolRegistration> registrations,
    IOptions<TraconOptions> options,
    ILogger<ToolRegistrationValidationService> logger) : IHostedService
{
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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
