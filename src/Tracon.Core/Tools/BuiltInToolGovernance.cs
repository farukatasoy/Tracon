using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Runs a tool that Tracon or the framework adds to a run itself through the same tool chain as a tool
/// registered in code.
/// </summary>
/// <remarks>
/// <para>
/// A tool added while a run is built (the file tools of the session workspace, the knowledge search, the search of
/// the conversation memory) is not in the tool registry, so it would run with none of the layers of
/// <see cref="ToolWrapperChain"/>: no authorization handler, no argument validation, no budget gate, no timeout, no
/// explained failure. This type applies the chain to such a tool with the same dependencies the registry uses, so a
/// layer added to the chain reaches these tools as well.
/// </para>
/// <para>
/// The tool stays out of <see cref="IToolRegistry"/>: it is bound while the agent is compiled and is not a catalog
/// tool. The authorization handler and the argument validator are still given a descriptor for it.
/// </para>
/// <para>
/// Approval is set here and not by the framework: the framework's own approval wrapper is turned off for the
/// workspace tools, so the order of the layers is the order of every other tool (approval inside the timeout,
/// next to the tool).
/// </para>
/// </remarks>
internal sealed class BuiltInToolGovernance
{
    private readonly IToolAuthorizationHandler _authorizationHandler;
    private readonly IToolArgumentsValidator _validator;
    private readonly IOptionsMonitor<TraconOptions> _options;
    private readonly IRunAttributionContext? _attribution;
    private readonly ILogger<AuthorizingAIFunction> _authorizingLogger;
    private readonly ILogger<TimeoutAIFunction> _timeoutLogger;
    private readonly ILogger<ValidatingAIFunction> _validatingLogger;

    /// <summary>Creates the governance with the dependencies of the tool chain.</summary>
    /// <param name="authorizationHandler">The authorization policy applied before every server-side call.</param>
    /// <param name="validator">The argument validation policy applied before every server-side call.</param>
    /// <param name="options">Supplies the installation's default tool timeout and output limit.</param>
    /// <param name="attribution">The run attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="authorizingLogger">The logger passed to every authorizing layer.</param>
    /// <param name="timeoutLogger">The logger passed to every timeout layer.</param>
    /// <param name="validatingLogger">The logger passed to every validating layer.</param>
    public BuiltInToolGovernance(
        IToolAuthorizationHandler authorizationHandler,
        IToolArgumentsValidator validator,
        IOptionsMonitor<TraconOptions> options,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> authorizingLogger,
        ILogger<TimeoutAIFunction> timeoutLogger,
        ILogger<ValidatingAIFunction> validatingLogger)
    {
        ArgumentNullException.ThrowIfNull(authorizationHandler);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorizingLogger);
        ArgumentNullException.ThrowIfNull(timeoutLogger);
        ArgumentNullException.ThrowIfNull(validatingLogger);

        _authorizationHandler = authorizationHandler;
        _validator = validator;
        _options = options;
        _attribution = attribution;
        _authorizingLogger = authorizingLogger;
        _timeoutLogger = timeoutLogger;
        _validatingLogger = validatingLogger;
    }

    /// <summary>Builds the governance from the final service-provider state.</summary>
    /// <param name="provider">The root service provider.</param>
    /// <returns>The governance.</returns>
    internal static BuiltInToolGovernance Create(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new BuiltInToolGovernance(
            provider.GetRequiredService<IToolAuthorizationHandler>(),
            provider.GetRequiredService<IToolArgumentsValidator>(),
            provider.GetRequiredService<IOptionsMonitor<TraconOptions>>(),
            provider.GetService<IRunAttributionContext>(),
            provider.GetRequiredService<ILogger<AuthorizingAIFunction>>(),
            provider.GetRequiredService<ILogger<TimeoutAIFunction>>(),
            provider.GetRequiredService<ILogger<ValidatingAIFunction>>());
    }

    /// <summary>Wraps a tool in the tool chain.</summary>
    /// <param name="function">The tool to wrap, with no approval layer of its own.</param>
    /// <param name="effect">The effect class the authorization handler and the run record see.</param>
    /// <param name="requiresApproval">Whether the call waits for a person's approval.</param>
    /// <returns>The wrapped tool.</returns>
    internal AIFunctionDeclaration Govern(AIFunction function, ToolEffect effect, bool requiresApproval)
    {
        ArgumentNullException.ThrowIfNull(function);

        var registration = new TraconToolRegistration(function)
        {
            RequiresApproval = requiresApproval,
            Effect = effect,
        };

        var descriptor = new ToolDescriptor
        {
            Name = function.Name,
            Description = function.Description,
            JsonSchema = function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                ? null
                : function.JsonSchema.GetRawText(),
            RequiresApproval = requiresApproval,
            RunsOnClient = false,
            Effect = effect,
        };

        return ToolWrapperChain.Compose(
            registration,
            descriptor,
            _authorizationHandler,
            _validator,
            _options.CurrentValue.Tools.DefaultTimeout,
            _options.CurrentValue.Tools.DefaultMaxOutputBytes,
            _attribution,
            _authorizingLogger,
            _timeoutLogger,
            _validatingLogger);
    }
}
