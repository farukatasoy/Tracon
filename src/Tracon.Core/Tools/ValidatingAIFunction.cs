using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="AIFunction"/> with an <see cref="IToolArgumentsValidator"/>
/// check that runs before every call.
/// </summary>
/// <remarks>
/// <para>
/// Installed by <see cref="ToolWrapperChain.Compose"/> one layer
/// <strong>inside</strong> the authorization wrapper and <strong>outside</strong>
/// the timeout wrapper: a caller who cannot make the call at all is turned
/// away before its arguments are even inspected, and a malformed call never
/// consumes a timeout budget or waits on a human approval.
/// </para>
/// <para>
/// A rejection <strong>throws</strong> <see cref="TraconException"/> with
/// the validator's reason as its message. This is the opposite choice from
/// <see cref="AuthorizingAIFunction"/>, which returns a denial as an ordinary
/// successful result: an authorization denial is a legitimate business
/// outcome, but rejected arguments are a malformed call, and Microsoft Agent
/// Framework's own conversion of a thrown exception into a
/// <c>FunctionResultContent</c> is what makes the run record it as
/// <c>ToolFailed</c> instead of an ordinary result.
/// </para>
/// <para>
/// If <see cref="IToolArgumentsValidator.ValidateAsync"/> throws, the call is
/// rejected (fail-closed) with a generic reason. A gate that fails open on an
/// exception is not a gate.
/// </para>
/// </remarks>
public sealed class ValidatingAIFunction : DelegatingAIFunction
{
    private readonly ToolDescriptor _descriptor;
    private readonly IToolArgumentsValidator _validator;
    private readonly ILogger<ValidatingAIFunction> _logger;

    /// <summary>Creates a new argument validation wrapper.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="descriptor">The tool's descriptor, passed to the validator.</param>
    /// <param name="validator">The validation policy.</param>
    /// <param name="logger">The logger for a faulting validator.</param>
    public ValidatingAIFunction(
        AIFunction innerFunction,
        ToolDescriptor descriptor,
        IToolArgumentsValidator validator,
        ILogger<ValidatingAIFunction> logger)
        : base(innerFunction)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(logger);

        _descriptor = descriptor;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ToolArgumentsValidationResult result;

        try
        {
            result = await _validator.ValidateAsync(_descriptor, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "The tool arguments validator threw while checking '{ToolName}'; the call was rejected (fail-closed).",
                Name);

            result = ToolArgumentsValidationResult.Invalid(
                "The argument validation check failed. This call was rejected and can be retried.");
        }

        if (!result.IsValid)
        {
            throw new TraconException(result.Reason ?? "This tool call's arguments were rejected.");
        }

        return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}
