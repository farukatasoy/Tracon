using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Calls the registered <see cref="IToolApprovalPresenter"/> for every pending
/// approval request and turns its fail-open contract into an actual guarantee.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is decoration, not a gate.</strong> Unlike
/// <c>IToolAuthorizationHandler</c> and <c>IToolArgumentsValidator</c>, a
/// presenter never blocks a call: a thrown exception, a call that overruns
/// <see cref="TraconToolOptions.ApprovalPresentationTimeout"/>, or no presenter
/// registered at all all resolve to <see langword="null"/> — the approval request is
/// published either way, with its raw arguments intact.
/// </para>
/// <para>
/// Called once per request from both the queue path (<c>AgentRunJobHandler</c>, to
/// populate <see cref="PendingApproval.Presentation"/>) and the recording path
/// (<see cref="RunRecordingAgent"/>, to build the closing run event's payload) —
/// through the SAME resolution, not two separate calls: <see cref="RunRecordingAgent"/>
/// resolves once and hands the result to
/// <see cref="TraconRunOptions.BeforePendingApprovalIsPublished"/>.
/// </para>
/// </remarks>
public sealed class ToolApprovalPresenterRunner(
    IToolApprovalPresenter presenter,
    IOptionsMonitor<TraconOptions> options,
    ILogger<ToolApprovalPresenterRunner> logger)
{
    private static readonly IReadOnlyDictionary<string, ToolApprovalPresentation?> Empty =
        new Dictionary<string, ToolApprovalPresentation?>(StringComparer.Ordinal);

    /// <summary>Resolves a presentation for every request, keyed by <c>RequestId</c>.</summary>
    /// <param name="requests">The pending requests found in the run's response.</param>
    /// <param name="tenantId">The calling tenant.</param>
    /// <param name="agentName">The agent that made the calls, when known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyDictionary<string, ToolApprovalPresentation?>> ResolveAllAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests,
        string tenantId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        // Skip everything, including building a ToolApprovalContext, when nothing was
        // ever going to come back — the overwhelming majority of installations, since
        // the default registration is NullToolApprovalPresenter.
        if (requests.Count == 0 || presenter is NullToolApprovalPresenter)
        {
            return Empty;
        }

        var resolved = new Dictionary<string, ToolApprovalPresentation?>(requests.Count, StringComparer.Ordinal);

        foreach (var request in requests)
        {
            resolved[request.RequestId] = await PresentOneAsync(request, tenantId, agentName, cancellationToken)
                .ConfigureAwait(false);
        }

        return resolved;
    }

    private async ValueTask<ToolApprovalPresentation?> PresentOneAsync(
        ToolApprovalRequestContent request,
        string tenantId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        var call = request.ToolCall as FunctionCallContent;

        var context = new ToolApprovalContext
        {
            TenantId = tenantId,
            ToolName = call?.Name ?? request.ToolCall.CallId,
            AgentName = agentName,
            Arguments = FunctionCallArguments.ToReadOnly(call?.Arguments),
        };

        var timeout = options.CurrentValue.Tools.ApprovalPresentationTimeout;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await presenter.PresentAsync(context, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired, not the caller's cancellation: fail open, per the
            // contract on IToolApprovalPresenter.PresentAsync.
            logger.LogWarning(
                "IToolApprovalPresenter timed out after {Timeout} resolving '{ToolName}'; " +
                "the approval request is published without a presentation.",
                timeout,
                context.ToolName);

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "IToolApprovalPresenter threw while resolving '{ToolName}'; the approval " +
                "request is published without a presentation.",
                context.ToolName);

            return null;
        }
    }
}
