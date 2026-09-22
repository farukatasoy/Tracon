using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="AIFunction"/> with an <see cref="IToolAuthorizationHandler"/>
/// check that runs before every call.
/// </summary>
/// <remarks>
/// <para>
/// Installed by the tool registry as the <strong>outermost</strong> layer —
///  If a
/// caller cannot make a call at all, there is no point waiting for its
/// timeout or asking a human to approve it.
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
/// If <see cref="IToolAuthorizationHandler.AuthorizeAsync"/> throws, the
/// call is denied (fail-closed). A gate that fails open on an exception is
/// not a gate.
/// </para>
/// </remarks>
internal sealed class AuthorizingAIFunction : DelegatingAIFunction
{
    private readonly IToolAuthorizationHandler _handler;
    private readonly ToolEffect _effect;
    private readonly string? _requiredPermission;
    private readonly IRunAttributionContext? _attribution;
    private readonly ILogger<AuthorizingAIFunction> _logger;

    /// <summary>Creates a new authorization wrapper.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="handler">The authorization policy.</param>
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
        ToolEffect effect,
        string? requiredPermission,
        IRunAttributionContext? attribution,
        ILogger<AuthorizingAIFunction> logger)
        : base(innerFunction)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(logger);

        _handler = handler;
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

        var request = new ToolAuthorizationRequest
        {
            ToolName = Name,
            Effect = _effect,
            RequiredPermission = _requiredPermission,
            TenantId = scope?.TenantId ?? "default",
            UserId = userId,
            RunId = scope?.RunId ?? Guid.Empty,
            AgentName = scope?.AgentName ?? Name,
        };

        ToolAuthorizationResult result;

        try
        {
            result = await _handler.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "The tool authorization handler threw while checking '{ToolName}'; the call was denied (fail-closed).",
                Name);

            result = ToolAuthorizationResult.Deny(
                "The authorization check failed. This call was denied and can be retried.");
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
