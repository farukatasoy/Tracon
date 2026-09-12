using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Wraps every agent resolved from the catalog with <see cref="StructuredResponseValidatingAgent"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Order"/> is 30, the <em>innermost</em> of the four built-in
/// decorators — closer to the compiled agent than run recording (0),
/// telemetry (10) and tool approval (20). Validation has to sit here and not
/// inside an <see cref="Microsoft.Extensions.AI.IChatClient"/> ring: the chat
/// client layer sees every tool-call turn, while a structured response only
/// ever arrives on the LAST one, and guessing "is this the last turn" from
/// function-call content would be fragile. The <see cref="AIAgent"/> layer
/// sees the final response directly.
/// </para>
/// </remarks>
internal sealed class StructuredResponseValidatingAgentDecorator : IAgentDecorator
{
    private readonly IStructuredResponseValidator _validator;
    private readonly IOptions<TraconStructuredResponseOptions> _options;
    private readonly ILogger<StructuredResponseValidatingAgent> _logger;

    /// <summary>Creates a new structured response validation decorator.</summary>
    /// <param name="validator">The validation policy.</param>
    /// <param name="options">The structured response settings.</param>
    /// <param name="logger">The logger for a faulting validator.</param>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    public StructuredResponseValidatingAgentDecorator(
        IStructuredResponseValidator validator,
        IOptions<TraconStructuredResponseOptions> options,
        ILogger<StructuredResponseValidatingAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _validator = validator;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(descriptor);

        return new StructuredResponseValidatingAgent(agent, descriptor, _validator, _options.Value, _logger);
    }
}
