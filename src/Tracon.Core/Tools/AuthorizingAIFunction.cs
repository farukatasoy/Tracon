using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="AIFunction"/> with the tenant's tool policy and an <see cref="IToolAuthorizationHandler"/>
/// check that run before every call.
/// </summary>
/// <remarks>
/// <para>
/// Installed by the tool registry as the <strong>outermost</strong> layer —
///  If a
/// caller cannot make a call at all, there is no point waiting for its
/// timeout or asking a human to approve it.
/// </para>
/// <para>
/// Two gates, in this order, and a call needs both: the tenant's tool policy (<see cref="TenantToolPolicyGate"/>,
/// the administrator's data rule) and then the handler (the installation's code rule). A refusal by the policy
/// never reaches the handler, so the consumer's code is not called for a call that cannot run.
/// </para>
/// <para>
/// A denial does <strong>not</strong> throw. Microsoft Agent Framework
/// turns a thrown exception into a tool result too, but denial and failure
/// are different things at the record level (the model-facing-text rule
/// applies here the same way): the reason text is returned as an ordinary
/// successful result, the call is marked in the tenant's authorization
/// accumulator so <c>ToolInvocationTracker</c> can tell a denial apart from
/// an ordinary success, and the model continues its turn.
/// </para>
/// <para>
/// If <see cref="IToolAuthorizationHandler.AuthorizeAsync"/> throws, or the policy store does, the
/// call is denied (fail-closed). A gate that fails open on an exception is
/// not a gate.
/// </para>
/// </remarks>
internal sealed class AuthorizingAIFunction : DelegatingAIFunction
{
    private readonly IToolAuthorizationHandler _handler;
    private readonly TenantToolPolicyGate _policyGate;
    private readonly ToolEffect _effect;
    private readonly string? _requiredPermission;
    private readonly IRunAttributionContext? _attribution;
    private readonly ILogger<AuthorizingAIFunction> _logger;

    /// <summary>Creates a new authorization wrapper.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="handler">The authorization policy.</param>
    /// <param name="policyGate">The tenant tool policy, checked before <paramref name="handler"/>.</param>
    /// <param name="effect">The tool's effect class, passed through to the handler.</param>
    /// <param name="requiredPermission">The tool's declared permission name, or <see langword="null"/>.</param>
    /// <param name="attribution">
    /// The current run's user attribution, or <see langword="null"/> when
    /// none is registered — the request's <c>UserId</c> stays empty in that case.
    /// </param>
    /// <param name="logger">The logger for a faulting authorization handler.</param>
    public AuthorizingAIFunction(
        AIFunction innerFunction,
        IToolAuthorizationHandler handler,
        TenantToolPolicyGate policyGate,
        ToolEffect effect,
        string? requiredPermission,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> logger)
        : base(innerFunction)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(policyGate);
        ArgumentNullException.ThrowIfNull(logger);

        _handler = handler;
        _policyGate = policyGate;
        _effect = effect;
        _requiredPermission = requiredPermission;
        _attribution = attribution;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var scope = TraconRunContext.Current;
        var (userId, _) = RunAttributionReader.Read(_attribution);
        var tenantId = scope?.TenantId ?? "default";

        ToolAuthorizationResult result;

        try
        {
            result = await _policyGate
                .CheckAsync(tenantId, Name, _requiredPermission, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogError(
                ex,
                "The tenant tool policy could not be read while checking '{ToolName}'; the call was denied (fail-closed).",
                Name);

            result = ToolAuthorizationResult.Deny(
                "The authorization check failed. This call was denied and can be retried.");
        }

        // The handler is asked only for a call the policy let through: the consumer's code does not run for a
        // call that cannot run.
        if (result.IsAllowed)
        {
            var request = new ToolAuthorizationRequest
            {
                ToolName = Name,
                Effect = _effect,
                RequiredPermission = _requiredPermission,
                TenantId = tenantId,
                UserId = userId,
                RunId = scope?.RunId ?? Guid.Empty,
                AgentName = scope?.AgentName ?? Name,
            };

            try
            {
                result = await _handler.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
            {
                _logger.LogError(
                    ex,
                    "The tool authorization handler threw while checking '{ToolName}'; the call was denied (fail-closed).",
                    Name);

                result = ToolAuthorizationResult.Deny(
                    "The authorization check failed. This call was denied and can be retried.");
            }
        }

        if (!result.IsAllowed)
        {
            if (FunctionInvokingChatClient.CurrentContext?.CallContent.CallId is { Length: > 0 } callId)
            {
                scope?.ToolAuthorization?.RecordDenied(callId);
            }

            return result.Reason ?? "This tool call was not authorized.";
        }

        return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}
