using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Applies <see cref="AgentPrismSessionOwnershipOptions"/> at the HTTP
/// boundary: narrows a listing to the caller, and refuses a single session
/// that belongs to somebody else.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="RunAuthorizationGate"/> and called the same way —
/// <strong>explicitly</strong>, from the body of every endpoint that reaches a
/// session, never as an endpoint filter. The endpoints that touch sessions do
/// not share one route shape, and each already produces its own
/// <c>404</c> body which a denial has to match byte for byte. A filter
/// could not write those bodies; this helper deliberately returns a
/// <see langword="bool"/> and lets each caller keep its own wording.
/// </para>
/// <para>
/// The two halves answer different questions on purpose:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="ResolveListOwnerFilterAsync"/> narrows a LISTING. A caller who
/// satisfies <see cref="AgentPrismSessionOwnershipOptions.ManagementPolicy"/>
/// gets today's whole-tenant listing, which is the only path by which a row
/// written before ownership existed stays reachable at all.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="DeniesAsync"/> guards ONE session on the session endpoints,
/// answering with each endpoint's own <c>404</c>. A management role buys no
/// access to an OWNED session here: it is a reason to see that a session
/// exists, never a reason to read one user's conversation as another user. It
/// does exempt the caller from
/// <see cref="AgentPrismSessionOwnershipOptions.RefuseUnownedSessions"/>,
/// which refuses a row that belongs to nobody — there no user's conversation
/// can leak, and the same caller already sees that row in the management
/// listing.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="CheckRunSessionAsync"/> guards the run-STARTING endpoints, which
/// reach a session through a different door — continuing a conversation reads
/// its whole history back into the model. It answers <c>403</c>, the shape a
/// denied run start already uses.
/// </description>
/// </item>
/// </list>
/// </remarks>
internal static class SessionOwnershipGate
{
    /// <summary>
    /// Resolves the owner a session listing must be narrowed to.
    /// </summary>
    /// <param name="options">The ownership settings, or <see langword="null"/> when unregistered.</param>
    /// <param name="attribution">The identity pipeline the caller is read from.</param>
    /// <param name="httpContext">The request, used to evaluate the management policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The owner to filter by, or <see langword="null"/> to apply no owner
    /// filter — which happens when ownership is off, or when the caller
    /// satisfies the management policy.
    /// </returns>
    /// <remarks>
    /// <strong>Fail-closed in both directions.</strong> With ownership on
    /// and no identity resolvable, this returns a value that matches NOTHING
    /// rather than <see langword="null"/>: returning <see langword="null"/>
    /// would hand an unidentified caller the whole tenant's listing, which is
    /// the exact leak the option exists to close. Every failure of the
    /// management-policy evaluation — unregistered policy, throwing handler,
    /// no authorization services at all — also lands on the narrow side.
    /// </remarks>
    public static async ValueTask<string?> ResolveListOwnerFilterAsync(
        IOptionsMonitor<AgentPrismSessionOwnershipOptions>? options,
        IRunAttributionContext? attribution,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (options?.CurrentValue is not { Enabled: true } settings)
        {
            return null;
        }

        if (await SatisfiesManagementPolicyAsync(settings, httpContext).ConfigureAwait(false))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (userId, _) = RunAttributionReader.Read(attribution);

        // A caller with no resolvable identity owns nothing, so they see
        // nothing. The sentinel is an id no session can carry: RunLabels caps
        // a real identity at MaxUserIdLength, and this is longer.
        return userId ?? UnresolvableOwnerSentinel;
    }

    /// <summary>
    /// Answers whether the caller must be refused access to one session.
    /// </summary>
    /// <param name="options">The ownership settings, or <see langword="null"/> when unregistered.</param>
    /// <param name="attribution">The identity pipeline the caller is read from.</param>
    /// <param name="store">The session store the stored owner is read from.</param>
    /// <param name="sessionId">The session being reached.</param>
    /// <param name="httpContext">The request, used to evaluate the management policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the caller must get the endpoint's own
    /// "session not found" answer; <see langword="false"/> when the access may
    /// proceed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Two cases deliberately do NOT deny, and both would break working
    /// deployments if they did:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <strong>The session does not exist.</strong> A session that was
    /// never opened is not refused here — the endpoint's own lookup
    /// answers it, and the voice endpoint legitimately opens a fresh session
    /// under an id nothing has written yet. Denying here would turn "not yet
    /// created" into "forbidden" and close that path.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <strong>The stored row is unowned, and the deployment has not asked for
    /// those to be refused.</strong> Every row written before ownership was
    /// turned on carries a <see langword="null"/> owner, and AgentPrism cannot
    /// invent one for a session it did not watch being opened. By default those
    /// rows keep exactly the tenant-wide reachability they had the day before
    /// the option was turned on; ownership is documented as not retroactive,
    /// and denying them would strand every conversation that was live at the
    /// moment of the flip. They are still absent from every owner-filtered
    /// LISTING, so they stop being discoverable.
    /// <see cref="AgentPrismSessionOwnershipOptions.RefuseUnownedSessions"/>
    /// turns that default around and refuses them, and a caller who satisfies
    /// the management policy is exempt from that refusal.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// What IS denied: the row carries an owner and the caller is not that
    /// owner — including a caller with no resolvable identity at all, who can
    /// prove ownership of nothing. No policy exempts a caller from that one.
    /// </para>
    /// </remarks>
    public static async ValueTask<bool> DeniesAsync(
        IOptionsMonitor<AgentPrismSessionOwnershipOptions>? options,
        IRunAttributionContext? attribution,
        ISessionStore store,
        string sessionId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(httpContext);

        if (options?.CurrentValue is not { Enabled: true } settings)
        {
            return false;
        }

        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            // Never opened. The endpoint's own lookup answers this, and
            // RefuseUnownedSessions deliberately does NOT reach here — see the
            // remarks above.
            return false;
        }

        if (record.OwnerId is not { } ownerId)
        {
            // An unowned row: written before ownership was turned on, and
            // AgentPrism cannot invent an owner for it. Reachable by default,
            // refused when the deployment asks for it — except to a caller who
            // satisfies the management policy, who already sees this row in the
            // management listing and would otherwise be left looking at a row
            // it cannot open. Every failure of that evaluation lands on the
            // refusing side, the same fail-closed direction as the listing.
            return settings.RefuseUnownedSessions &&
                   !await SatisfiesManagementPolicyAsync(settings, httpContext).ConfigureAwait(false);
        }

        var (userId, _) = RunAttributionReader.Read(attribution);

        return !string.Equals(ownerId, userId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Answers whether a run that names a session may start.
    /// </summary>
    /// <param name="options">The ownership settings, or <see langword="null"/> when unregistered.</param>
    /// <param name="attribution">The identity pipeline the caller is read from.</param>
    /// <param name="store">The session store the stored owner is read from.</param>
    /// <param name="sessionId">The session the run would continue, or <see langword="null"/> for a sessionless run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>403</c> to return if the run may not start; otherwise <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Checked at the run-STARTING endpoints, and not only at the session
    /// endpoints, because a run is a way of reading a session: continuing
    /// somebody else's conversation replays its history into the model and
    /// appends to it. Gating <c>GET /api/sessions/{id}</c> alone would leave
    /// the wider door open.
    /// </para>
    /// <para>
    /// It is also checked HERE rather than inside
    /// <c>AgentSessionManager</c>, where the invariant would cover every
    /// caller at once. The manager serves background work too — an approval
    /// resume, a run continuation — and that work has no caller whose identity
    /// could be compared against the row. Refusing there would fail those jobs
    /// permanently on every retry. "Whose request is this" is a question only
    /// the HTTP boundary can ask.
    /// </para>
    /// <para>
    /// Three answers, deliberately: an owned session that is not the caller's
    /// is refused; a session that does not exist yet is refused only when
    /// nothing can be resolved to own it; and an UNOWNED row is refused only
    /// while
    /// <see cref="AgentPrismSessionOwnershipOptions.RefuseUnownedSessions"/> is
    /// on, with no management exemption — that exemption lets support READ a
    /// legacy conversation, not append to one. A run against a FRESH id by an
    /// identified caller proceeds and claims the session, which is how every
    /// owned session is born.
    /// </para>
    /// <para>
    /// A refusal here confirms that the named session exists — a fresh id
    /// proceeds, an owned one does not. That is unavoidable on this surface:
    /// the alternative is to refuse fresh ids too, which would forbid opening
    /// any session at all. The identity-hiding <c>404</c> is kept where it CAN
    /// be kept, on the session resource endpoints, and the run surface follows
    /// the <c>403</c> shape a denied run start already uses.
    /// </para>
    /// </remarks>
    public static async ValueTask<ProblemHttpResult?> CheckRunSessionAsync(
        IOptionsMonitor<AgentPrismSessionOwnershipOptions>? options,
        IRunAttributionContext? attribution,
        ISessionStore store,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (options?.CurrentValue is not { Enabled: true } settings ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var (userId, _) = RunAttributionReader.Read(attribution);

        if (record is null)
        {
            // The run would OPEN this session. Refusing an unidentified caller
            // here is the same decision AgentSessionManager makes at the write
            // itself; making it now means the model is never called for a turn
            // whose session cannot be kept.
            return settings.RequireAuthenticatedOwner && userId is null
                ? Refused(
                    "Session owner required",
                    $"Session '{sessionId}' cannot be opened: session ownership is on and no " +
                    "authenticated identity could be resolved to own it.")
                : null;
        }

        if (record.OwnerId is not { } ownerId)
        {
            // An unowned row from before ownership was turned on. Reachable by
            // default; refused outright when the deployment asks for it.
            //
            // 🚨 No management exemption here, unlike DeniesAsync. That
            // exemption exists so support can READ a row it already sees in the
            // management listing; starting a run APPENDS to the conversation,
            // and an operator continuing somebody's conversation as themselves
            // is not the same act as reading it.
            return settings.RefuseUnownedSessions ? RefusedAsAnotherUsers() : null;
        }

        if (string.Equals(ownerId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        // 🚨 The SAME refusal an unowned row gets above, word for word.
        // Wording that told the two apart would let a caller learn, from a
        // response it is already allowed to see, which sessions predate
        // ownership — and the client's action is identical either way: this
        // session cannot carry the run.
        return RefusedAsAnotherUsers();
    }

    /// <summary>
    /// Builds the single refusal both "not yours" and "nobody's" answer with
    /// on a run-starting endpoint.
    /// </summary>
    /// <returns>The <c>403</c> to return.</returns>
    private static ProblemHttpResult RefusedAsAnotherUsers()
        => Refused(
            "Session not authorized",
            "The session named in this request belongs to another user.");

    /// <summary>
    /// Builds the <c>403</c> an ownership refusal returns from a run-starting
    /// endpoint.
    /// </summary>
    /// <param name="title">The problem title.</param>
    /// <param name="detail">The problem detail.</param>
    /// <returns>The problem response.</returns>
    /// <remarks>
    /// The <c>errorType</c> extension is what makes the refusal MACHINE
    /// readable, and it has to be the same value
    /// <see cref="AgentPrismSessionOwnerRequiredException"/> writes when the
    /// same refusal is raised deeper, at the write itself. Two spellings of one
    /// decision would force a client to match on prose.
    /// </remarks>
    private static ProblemHttpResult Refused(string title, string detail)
        => TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errorType"] = AgentPrismSessionOwnerRequiredException.SessionOwnerRequiredErrorType,
            });

    /// <summary>
    /// An owner value no real session can carry, used to make an
    /// unidentified caller's listing empty instead of unfiltered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Longer than <see cref="RunLabels.MaxUserIdLength"/>, so
    /// <see cref="RunAttributionReader"/> would have rejected it as an
    /// identity long before it could ever be written to a row.
    /// </para>
    /// <para>
    /// The filler is written as an ESCAPE and is deliberately neither a space
    /// nor a raw control character. A space-only sentinel would match a row
    /// whose owner is an empty string or a run of spaces on SQL Server, which
    /// ignores trailing spaces when comparing <c>nvarchar</c> (ANSI padding) -
    /// silently, on that provider alone, and in exactly the listing this value
    /// exists to keep empty. A raw unprintable byte would compare correctly but
    /// makes the source itself unreadable: tools treat a file containing one as
    /// binary and stop reporting matches in it, so the next reader cannot even
    /// grep for this field.
    /// </para>
    /// </remarks>
    private static readonly string UnresolvableOwnerSentinel =
        new('\uFFFD', RunLabels.MaxUserIdLength + 1);

    /// <summary>
    /// Evaluates the management policy for the current caller, failing closed
    /// on every uncertainty.
    /// </summary>
    /// <param name="settings">The ownership settings.</param>
    /// <param name="httpContext">The request.</param>
    /// <returns><see langword="true"/> only when the policy is registered AND succeeds.</returns>
    private static async ValueTask<bool> SatisfiesManagementPolicyAsync(
        AgentPrismSessionOwnershipOptions settings,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(settings.ManagementPolicy))
        {
            return false;
        }

        var authorization = httpContext.RequestServices.GetService<IAuthorizationService>();

        if (authorization is null)
        {
            return false;
        }

        try
        {
            var result = await authorization
                .AuthorizeAsync(httpContext.User, resource: null, settings.ManagementPolicy)
                .ConfigureAwait(false);

            return result.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unregistered policy name throws InvalidOperationException, and
            // a consumer's own requirement handler can throw anything. Neither
            // may hand out an unfiltered listing.
            return false;
        }
    }
}
