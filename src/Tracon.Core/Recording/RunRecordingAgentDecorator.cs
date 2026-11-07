using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps every agent resolved from the catalog with <see cref="RunRecordingAgent"/>.
/// </summary>
/// <remarks>
/// <see cref="Order"/> is 0, so run recording is the outermost wrapper and
/// also measures the time spent by inner wrappers.
/// </remarks>
internal sealed class RunRecordingAgentDecorator : IAgentDecorator
{
    private readonly RunRecordingServices _services;
    private readonly ILogger<RunRecordingAgent> _logger;

    /// <summary>Creates a new recording decorator.</summary>
    /// <param name="services">
    /// The services and settings every recorder of a run shares. The container builds all of them
    /// (<see cref="RunRecordingServices.Create"/>); a member left out of a hand-built instance switches its feature off.
    /// </param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">One of the arguments is <see langword="null"/>.</exception>
    public RunRecordingAgentDecorator(RunRecordingServices services, ILogger<RunRecordingAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return new RunRecordingAgent(
            agent,
            _services,
            new RecordedAgentIdentity(
                // The model name comes from the catalog descriptor. It may be
                // unknown for code-defined agents; in that case the run record
                // carries no model and is excluded from the model breakdown.
                descriptor?.Model?.Model,

                // The provider is used only for cost resolution and is not
                // persisted.
                descriptor?.Model?.Provider,
                descriptor?.Version),
            _logger);
    }
}
