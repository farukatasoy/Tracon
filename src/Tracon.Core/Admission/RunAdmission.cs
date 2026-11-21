using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Asks the three questions Tracon's own HTTP endpoints ask before a run starts, for a caller that runs an
/// agent in process: may this user start this agent, may this user continue this session, and does the tenant
/// still have quota.
/// </summary>
/// <remarks>
/// <para>
/// An agent obtained from <see cref="IAgentCatalog.ResolveAsync"/> and run with <c>RunAsync</c> is recorded
/// and its tools are authorized, but nothing in that path asks these questions: the checks sit in front of
/// the HTTP endpoints, because the work that starts without a caller (queued runs, approval resumes,
/// scheduled triggers) has no identity to check. A host that runs agents on behalf of a user calls
/// <see cref="CheckAsync"/> itself, first, and starts the run only when the answer allows it.
/// </para>
/// <para>
/// The answer is the same one the HTTP endpoints give: the registered <see cref="IRunAuthorizationHandler"/>
/// decides first (a handler that throws denies the run), then session ownership, then the tenant's quota.
/// The tenant comes from <see cref="ITenantContext"/>. The user comes from <see cref="AmbientRunAttributionScope"/>
/// when one is open and otherwise from <see cref="IRunAttributionContext"/>, which is the identity the session
/// manager records as the owner of a session it opens, so the user who passes the check is the user who owns
/// the session.
/// </para>
/// <para>
/// Nothing here runs a model, opens a session or spends quota. The quota is read at the door and counts runs
/// that already ended, so a burst of simultaneous callers can pass it together by the work that was running when
/// the limit was reached.
/// </para>
/// </remarks>
public sealed class RunAdmission
{
    private readonly IRunAuthorizationHandler _handler;
    private readonly ITenantContext _tenants;
    private readonly IRunAttributionContext? _attribution;
    private readonly IOptionsMonitor<TraconSessionOwnershipOptions>? _ownership;
    private readonly ISessionStore _sessions;
    private readonly QuotaEnforcer? _quota;
    private readonly ILogger _logger;

    internal RunAdmission(
        IRunAuthorizationHandler handler,
        ITenantContext tenants,
        IRunAttributionContext? attribution,
        IOptionsMonitor<TraconSessionOwnershipOptions>? ownership,
        ISessionStore sessions,
        QuotaEnforcer? quota,
        ILogger<RunAdmission>? logger)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(sessions);

        _handler = handler;
        _tenants = tenants;
        _attribution = attribution;
        _ownership = ownership;
        _sessions = sessions;
        _quota = quota;
        _logger = logger ?? NullLogger<RunAdmission>.Instance;
    }

    /// <summary>Checks whether the current user may start a run of an agent, optionally on a session.</summary>
    /// <param name="agentName">The name of the agent the run would start.</param>
    /// <param name="sessionId">
    /// The session the run would continue or open, or <see langword="null"/> for a run that keeps no session.
    /// </param>
    /// <param name="cancellationToken">The cancellation token of the caller.</param>
    /// <returns>
    /// The decision. A refusal is a value, not an exception: it is an expected answer, as it is for
    /// <see cref="IRunAuthorizationHandler"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="agentName"/> is blank.</exception>
    /// <exception cref="OperationCanceledException">The caller's own token was canceled.</exception>
    public async ValueTask<RunAdmissionDecision> CheckAsync(
        string agentName,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var tenantId = _tenants.TenantId;
        var userId = ReadUserId();

        var request = new RunAuthorizationRequest
        {
            TenantId = tenantId,
            AgentName = agentName,
            SessionId = sessionId,
            UserId = userId,
            Access = RunAccess.Start,
        };

        var outcome = await RunAdmissionRules.AuthorizeStartAsync(_handler, request, cancellationToken).ConfigureAwait(false);

        if (outcome.Failure is { } failure)
        {
            _logger.LogError(
                failure,
                "The registered run authorization handler threw while checking agent '{AgentName}' for tenant '{TenantId}'; the run is denied.",
                agentName,
                tenantId);

            return Denied(RunAdmissionDenial.NotAuthorized, RunAdmissionRules.FailedCheckDetail);
        }

        if (!outcome.IsAllowed)
        {
            return Denied(RunAdmissionDenial.NotAuthorized, outcome.Reason ?? RunAdmissionRules.HandlerDeniedDetail);
        }

        var ownership = await RunAdmissionRules
            .CheckSessionAsync(_ownership?.CurrentValue, _sessions, sessionId, () => userId, cancellationToken)
            .ConfigureAwait(false);

        switch (ownership)
        {
            case SessionOwnershipVerdict.OwnerRequired:
                return Denied(
                    RunAdmissionDenial.SessionOwnerRequired,
                    RunAdmissionRules.SessionOwnerRequiredDetail(sessionId!));

            case SessionOwnershipVerdict.NotAuthorized:
                return Denied(RunAdmissionDenial.SessionNotAuthorized, RunAdmissionRules.SessionNotAuthorizedDetail);

            case SessionOwnershipVerdict.Allowed:
            default:
                break;
        }

        if (_quota is null)
        {
            return RunAdmissionDecision.Allowed;
        }

        var quota = await _quota.CheckAsync(tenantId, agentName, cancellationToken).ConfigureAwait(false);

        return quota.IsAllowed
            ? RunAdmissionDecision.Allowed
            : new RunAdmissionDecision
            {
                IsAllowed = false,
                Denial = RunAdmissionDenial.QuotaExceeded,
                Reason = quota.Reason ?? RunAdmissionRules.QuotaExceededDetail,
                QuotaMetric = quota.Metric,
                QuotaPeriod = quota.Period,
                QuotaLimit = quota.Limit,
                QuotaUsed = quota.Used,
                QuotaResetsAt = quota.ResetsAt,
                QuotaAgentName = quota.AgentName,
            };
    }

    private static RunAdmissionDecision Denied(RunAdmissionDenial denial, string reason)
        => new() { IsAllowed = false, Denial = denial, Reason = reason };

    private string? ReadUserId()
    {
        if (AmbientRunAttributionScope.IsActive)
        {
            return AmbientRunAttributionScope.CurrentUserId;
        }

        var (userId, _) = RunAttributionReader.Read(
            _attribution,
            (message, exception) => _logger.LogWarning(
                exception,
                "{Message} The admission check treats the caller as having no identity.",
                message));

        return userId;
    }
}
