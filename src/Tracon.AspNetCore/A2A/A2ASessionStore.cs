using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

// MAAI001: AgentSessionStore is marked "evaluation purposes only" since
// Microsoft Agent Framework 1.22 (same rationale as TraconAgentSessionStore,
// K-1016). This type is internal, so the experimental base never reaches
// Tracon's public API; the suppression covers this file only.
#pragma warning disable MAAI001

/// <summary>
/// Stores the sessions of A2A conversations, one session per tenant, agent,
/// caller and <c>contextId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework's <c>IsolationKeyScopedAgentSessionStore</c> wraps
/// this store and adds the <c>isolation</c> partition from
/// <see cref="TraconCallerIsolationKeyProvider"/>. This class turns that
/// partitioned key into the single identity Tracon keeps sessions under, and
/// hands the rest to <see cref="TraconAgentSessionStore"/>: the same tenant
/// boundary and state version as every other run surface.
/// </para>
/// <para>
/// Session ownership is a second door behind the isolation key. Isolation
/// keeps callers that Tracon can tell apart in separate conversations. Ownership
/// answers a different question — which END USER a conversation belongs to — and
/// one caller (a gateway holding one API key) can speak for many users. So a
/// message that resumes a stored conversation is checked against its owner, the
/// same check the other run-starting endpoints make, before the history reaches
/// the model.
/// </para>
/// <para>
/// The identity is a digest, not a readable composition. The wire
/// <c>contextId</c> has no length limit and the <c>sessions</c> identity column
/// does (SQL Server: <c>nvarchar(200)</c>). The same <c>contextId</c> on two
/// agents would restore one agent's state into the other. The tenant is part of
/// the digest too: the table keys a row by tenant and identity, but a lookup by
/// identity alone must find ONE tenant. The <c>a2a:</c> prefix keeps the
/// identity apart from the ones other surfaces write.
/// </para>
/// <para>
/// The format is persisted data. Changing it leaves every stored conversation
/// unreachable, so a test pins known values.
/// </para>
/// </remarks>
internal sealed class A2ASessionStore : DelegatingAgentSessionStore
{
    /// <summary>Starts every A2A session identity.</summary>
    internal const string SessionIdPrefix = "a2a:";

    private const string IsolationPartitionName = "isolation";

    private readonly ITenantContext _tenantContext;
    private readonly ISessionStore _sessions;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<TraconSessionOwnershipOptions>? _ownershipOptions;
    private readonly IRunAttributionContext? _attribution;

    /// <summary>Creates a new store.</summary>
    /// <param name="innerStore">The store the rewritten key is passed to.</param>
    /// <param name="tenantContext">The tenant of the current request.</param>
    /// <param name="sessions">Reads the owner of a stored conversation.</param>
    /// <param name="httpContextAccessor">Reads the current request.</param>
    /// <param name="ownershipOptions">The ownership settings; <see langword="null"/> means ownership is off.</param>
    /// <param name="attribution">The identity pipeline the end user is read from.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public A2ASessionStore(
        AgentSessionStore innerStore,
        ITenantContext tenantContext,
        ISessionStore sessions,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions = null,
        IRunAttributionContext? attribution = null)
        : base(innerStore)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _tenantContext = tenantContext;
        _sessions = sessions;
        _httpContextAccessor = httpContextAccessor;
        _ownershipOptions = ownershipOptions;
        _attribution = attribution;
    }

    /// <summary>
    /// Registers the store of one exposed agent, unless the consumer already did.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="agentName">The exposed agent.</param>
    /// <remarks>
    /// <para>
    /// The consumer's registration wins, in this order: a store registered under
    /// the agent's name, then a store registered without a key, then this one.
    /// A consumer store receives the partitioned key as it is and handles the
    /// <c>isolation</c> partition and the identity length itself.
    /// </para>
    /// <para>
    /// Microsoft Agent Framework looks the store up under the agent's name when
    /// the A2A server is built (<c>MapTraconA2A</c>), so a keyed registration is
    /// the only seam it offers.
    /// </para>
    /// </remarks>
    internal static void Register(IServiceCollection services, string agentName)
        => services.TryAddKeyedSingleton<AgentSessionStore>(
            agentName,
            static (provider, _) => provider.GetService<AgentSessionStore>()
                ?? new A2ASessionStore(
                    new TraconAgentSessionStore(
                        provider.GetRequiredService<AgentSessionManager>(),
                        provider.GetRequiredService<SessionDeletion>()),
                    provider.GetRequiredService<ITenantContext>(),
                    provider.GetRequiredService<ISessionStore>(),
                    provider.GetRequiredService<IHttpContextAccessor>(),
                    provider.GetService<IOptionsMonitor<TraconSessionOwnershipOptions>>(),
                    provider.GetService<IRunAttributionContext>()));

    /// <summary>Builds the identity a conversation is stored under.</summary>
    /// <param name="tenantId">The tenant of the request.</param>
    /// <param name="agentName">The exposed agent.</param>
    /// <param name="isolationKey">The caller, from <see cref="TraconCallerIsolationKeyProvider"/>.</param>
    /// <param name="contextId">The <c>contextId</c> the caller sent.</param>
    /// <returns><c>a2a:</c> followed by 64 lowercase hex characters.</returns>
    /// <remarks>
    /// Each part is hashed with its length in front of it. A separator character
    /// would not do: a <c>contextId</c> is caller input, and one that carries the
    /// separator moves the boundary between two parts (<c>"a", "b\u001Fc"</c> and
    /// <c>"a\u001Fb", "c"</c> concatenate to the same text).
    /// </remarks>
    internal static string SessionId(string tenantId, string agentName, string isolationKey, string contextId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Append(hash, tenantId);
        Append(hash, agentName);
        Append(hash, isolationKey);
        Append(hash, contextId);

        return SessionIdPrefix + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <inheritdoc />
    /// <exception cref="TraconSessionOwnerRequiredException">The conversation belongs to another user.</exception>
    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        var scoped = Rewrite(agent, key);

        await EnsureAvailableToCallerAsync(scoped.SessionId, cancellationToken).ConfigureAwait(false);

        return await InnerStore.GetSessionAsync(agent, scoped, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="TraconSessionOwnerRequiredException">
    /// The conversation belongs to another user, or ownership is on and no user can be resolved to own a new one.
    /// </exception>
    public override async ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        var scoped = Rewrite(agent, key);

        await EnsureAvailableToCallerAsync(scoped.SessionId, cancellationToken).ConfigureAwait(false);

        return await InnerStore.GetOrCreateSessionAsync(agent, scoped, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override ValueTask SaveSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => InnerStore.SaveSessionAsync(agent, Rewrite(agent, key), session, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <c>DelegatingAgentSessionStore</c> forwards the key as it is. That would delete the session of a
    /// plain <c>caller::contextId</c> identity (what the bridge folds an isolation partition into) and
    /// leave the conversation this store wrote under its digest. The key is rewritten like every other
    /// operation's, and a conversation that belongs to another user is refused before anything is deleted.
    /// </remarks>
    /// <exception cref="TraconSessionOwnerRequiredException">The conversation belongs to another user.</exception>
    public override async ValueTask DeleteSessionAsync(
        AIAgent agent,
        AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        var scoped = Rewrite(agent, key);

        await EnsureMayDeleteAsync(scoped.SessionId, cancellationToken).ConfigureAwait(false);

        await InnerStore.DeleteSessionAsync(agent, scoped, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a delete of a conversation that belongs to another user, before anything is deleted.
    /// </summary>
    /// <remarks>
    /// The question the delete endpoints ask (<see cref="SessionOwnershipGate.DeniesAsync"/>), not the one
    /// a run asks: a conversation that does not exist is not refused, because deleting it is a no-op, and
    /// a caller who satisfies the management policy may delete an unowned conversation.
    /// </remarks>
    private async ValueTask EnsureMayDeleteAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext is not { } httpContext)
        {
            return;
        }

        if (await SessionOwnershipGate
                .DeniesAsync(_ownershipOptions, _attribution, _sessions, sessionId, httpContext, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new TraconSessionOwnerRequiredException("This session is not available to the calling user.")
            {
                SessionId = sessionId,
            };
        }
    }

    /// <summary>
    /// Refuses a stored conversation that belongs to another user, before its
    /// history can reach the model.
    /// </summary>
    /// <remarks>
    /// Asked here and not in <c>AgentSessionManager</c> for the reason the other
    /// run surfaces ask it at their HTTP edge: the manager also serves background
    /// work that has no caller to compare against the row. A request without an
    /// HTTP context has no such caller either, so it is not asked.
    /// </remarks>
    private async ValueTask EnsureAvailableToCallerAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext is not { } httpContext)
        {
            return;
        }

        if (await SessionOwnershipGate
                .CheckRunSessionAsync(_ownershipOptions, _attribution, _sessions, sessionId, httpContext, cancellationToken)
                .ConfigureAwait(false) is { } refusal)
        {
            throw new TraconSessionOwnerRequiredException(
                refusal.ProblemDetails.Detail ?? "This session is not available to the calling user.")
            {
                SessionId = sessionId,
            };
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];

        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    /// <summary>Replaces the partitioned key with the identity the session is stored under.</summary>
    /// <exception cref="InvalidOperationException">The key has no <c>isolation</c> partition.</exception>
    /// <exception cref="NotSupportedException">The key carries a partition other than <c>isolation</c>.</exception>
    private AgentSessionStoreKey Rewrite(AIAgent agent, AgentSessionStoreKey key)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Partitions is not { Count: > 0 } partitions)
        {
            // Fail closed. Storing the conversation under the bare contextId
            // would hand it to every caller that sends the same value.
            throw new InvalidOperationException(
                "An A2A conversation is stored per caller, and this key carries no isolation partition. " +
                "Register an AgentIsolationKeyProvider, or remove the replacement that bypasses it.");
        }

        if (partitions.Count != 1 || !partitions.TryGetValue(IsolationPartitionName, out var isolationKey))
        {
            throw new NotSupportedException(
                $"The A2A session store supports only the '{IsolationPartitionName}' partition; " +
                $"received: {string.Join(", ", partitions.Keys)}.");
        }

        return new AgentSessionStoreKey(
            SessionId(_tenantContext.TenantId, agent.Name ?? agent.Id, isolationKey, key.SessionId));
    }
}
#pragma warning restore MAAI001
