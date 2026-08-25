using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>Validates the structural agent-source contract while the host starts.</summary>
internal sealed class AgentSourceValidationService(
    IEnumerable<IAgentSource> sources,
    ILogger<AgentSourceValidationService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var names = new Dictionary<string, IAgentSource>(StringComparer.OrdinalIgnoreCase);
        var priorities = new Dictionary<int, IAgentSource>();

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                throw new AgentPrismAgentSourceException(
                    source.GetType().Name,
                    AgentPrismAgentSourceException.SourceContractErrorType,
                    $"Agent source '{source.GetType().FullName}' has an empty name.");
            }

            if (!names.TryAdd(source.Name, source))
            {
                throw new AgentPrismAgentSourceException(
                    source.Name,
                    AgentPrismAgentSourceException.SourceContractErrorType,
                    $"Agent sources '{names[source.Name].GetType().FullName}' and '{source.GetType().FullName}' share the name '{source.Name}'.");
            }

            if (!priorities.TryAdd(source.Priority, source))
            {
                logger.LogWarning(
                    "Agent sources '{FirstSource}' and '{SecondSource}' share priority {Priority}. DI registration order resolves ties.",
                    priorities[source.Priority].Name,
                    source.Name,
                    source.Priority);
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
