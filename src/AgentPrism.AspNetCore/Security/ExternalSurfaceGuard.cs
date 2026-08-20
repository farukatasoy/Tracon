using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Startup checks that the MCP and A2A external surfaces share.
/// </summary>
/// <remarks>
/// The goal is the same application of the no-surprises rule: an explicit failure instead of an external
/// surface that silently half works. <see cref="EnsureRemoteAccessNotCombined"/> runs at
/// the moment of the <c>MapAgentPrismMcpServer</c>/<c>MapAgentPrismA2A</c> call (endpoint
/// mapping, BEFORE <c>app.Run()</c>) and does not touch the database. The approval guard
/// (<see cref="EnsureNoApprovalRequiredTools"/>) does touch the database, so it runs from
/// <c>McpApprovalGuardFilter</c>/<c>A2AApprovalGuardFilter</c> inside a Task that STARTS at
/// endpoint mapping time but waits in the background until the schema is ready — this
/// prevents a crash with "no such table" on an empty database.
/// This Task is awaited BEFORE every request; no request can get ahead of the check.
/// </remarks>
internal static class ExternalSurfaceGuard
{
    private static readonly TimeSpan MaxCatalogListDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the catalog, retrying until the application shuts down, because the schema may
    /// not be queryable yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// with <c>AutoApplyMigrations=false</c>,
    /// <see cref="SchemaReadyGate.MarkReady"/> is called without proving that the schema is
    /// REALLY queryable (preparing the schema is the responsibility of the consumer — see
    /// the note on <see cref="SchemaReadyGate.MarkReady"/> itself). An external migration
    /// step that creates the schema may not have finished when the application starts, and
    /// it may never run during the lifetime of the application (the operator can choose to
    /// apply the migration by hand, later). Such a store failure must NOT stop the run
    /// IMMEDIATELY ("a store failure does not interrupt the run") — the contract of
    /// the persistence layer asks for the same: the application stays up and <c>/health</c>
    /// reports <c>Unhealthy</c>. The bound is therefore not a retry COUNT, only the
    /// <see cref="IHostApplicationLifetime.ApplicationStopping"/> token of
    /// <paramref name="lifetime"/> — if the schema is never prepared, only requests that
    /// arrive at the externally exposed A2A/MCP surface wait on this Task (until the
    /// application shuts down); the rest of the application (agent CRUD, the health
    /// endpoint, and so on) stays usable right away and is NEVER stopped with
    /// <see cref="IHostApplicationLifetime.StopApplication"/>.
    /// </para>
    /// </remarks>
    /// <param name="catalog">The catalog to query.</param>
    /// <param name="lifetime">The application lifetime — the retry interval and the cancellation are read from it.</param>
    /// <param name="logger">The logger that records intermediate attempts as warnings.</param>
    /// <param name="protocol">The external surface name that appears in the log message (<c>"A2A"</c>/<c>"MCP"</c>).</param>
    /// <returns>The catalog descriptors.</returns>
    public static async Task<IReadOnlyList<AgentDescriptor>> ListCatalogWithRetryAsync(
        IAgentCatalog catalog,
        IHostApplicationLifetime lifetime,
        ILogger logger,
        string protocol)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await catalog.ListAsync(lifetime.ApplicationStopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "The catalog query for the {Protocol} external surface check failed on attempt " +
                    "{Attempt}; the schema may not be queryable yet (with AutoApplyMigrations=false " +
                    "an external migration step may still be pending). Retrying in " +
                    "{DelayMilliseconds} ms; the rest of the application stays operational.",
                    protocol,
                    attempt,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, lifetime.ApplicationStopping).ConfigureAwait(false);

                var next = delay * 2;
                delay = next < MaxCatalogListDelay ? next : MaxCatalogListDelay;
            }
        }
    }

    /// <summary>Determines whether an agent is on the allow list.</summary>
    /// <param name="agentName">The agent name to check.</param>
    /// <param name="exposedAgents">The allow list.</param>
    /// <param name="exposeAll">Whether every agent is exposed.</param>
    /// <returns><see langword="true"/> when the agent is exposed.</returns>
    public static bool IsExposed(string agentName, ICollection<string> exposedAgents, bool exposeAll)
        => exposeAll || exposedAgents.Contains(agentName, StringComparer.Ordinal);

    /// <summary>
    /// Prevents an external surface from being exposed while <c>AllowRemoteAccess</c> is on
    /// and the system holds no valid key with the
    /// <see cref="ApiKeyScope.ExternalInvoke"/> scope.
    /// </summary>
    /// <param name="allowRemoteAccess">The <see cref="AgentPrismEndpointOptions.AllowRemoteAccess"/> value.</param>
    /// <param name="protocol">The name of the external surface being exposed.</param>
    /// <param name="apiKeyStore">The key store.</param>
    /// <exception cref="InvalidOperationException">
    /// When it is called while remote access is on and no valid <c>external:invoke</c> key exists.
    /// </exception>
    /// <remarks>
    /// <para>
    /// An API key makes the lock CONDITIONAL, it does not REMOVE it:
    /// a single static bearer token is not enough to protect an agent surface exposed beyond
    /// loopback, but a per-tenant API key with the <c>external:invoke</c> scope is.
    /// </para>
    /// <para>
    /// The synchronous call is DELIBERATE: this check runs once at startup, outside
    /// request processing (the same rationale as
    /// <see cref="AgentPrismMcpServerExtensions.MapAgentPrismMcpServer"/>).
    /// </para>
    /// </remarks>
    public static void EnsureRemoteAccessNotCombined(bool allowRemoteAccess, string protocol, IApiKeyStore apiKeyStore)
    {
        ArgumentNullException.ThrowIfNull(apiKeyStore);

        if (!allowRemoteAccess)
        {
            return;
        }

        var hasExternalInvokeKey = apiKeyStore
            .HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (hasExternalInvokeKey)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{protocol} cannot be exposed while AllowRemoteAccess is on: the system holds no " +
            "API key with the 'external:invoke' scope that is neither expired nor revoked. A " +
            "single static bearer token is not enough to protect an agent surface exposed " +
            "beyond loopback. Create a key with the 'external:invoke' scope through " +
            "'POST /api/api-keys'.");
    }

    /// <summary>
    /// Verifies that none of the agents to be exposed carries a tool that requires approval.
    /// </summary>
    /// <param name="descriptors">The agent descriptors in the catalog.</param>
    /// <param name="exposedAgents">The allow list.</param>
    /// <param name="exposeAll">Whether every agent is exposed.</param>
    /// <param name="toolRegistry">The tool registry that carries the approval flag.</param>
    /// <param name="protocol">The name of the external surface being exposed.</param>
    /// <exception cref="InvalidOperationException">
    /// When an exposed agent carries a tool that requires approval.
    /// </exception>
    /// <remarks>
    /// Enforces the same boundary at startup: an external caller is not a human and
    /// cannot answer an approval request. Running silently without approval is unacceptable.
    /// </remarks>
    public static void EnsureNoApprovalRequiredTools(
        IReadOnlyList<AgentDescriptor> descriptors,
        ICollection<string> exposedAgents,
        bool exposeAll,
        IToolRegistry toolRegistry,
        string protocol)
    {
        var approvalRequiredToolNames = toolRegistry.List()
            .Where(static tool => tool.RequiresApproval)
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (approvalRequiredToolNames.Count == 0)
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            if (!IsExposed(descriptor.Name, exposedAgents, exposeAll))
            {
                continue;
            }

            var offending = descriptor.ToolNames.Where(approvalRequiredToolNames.Contains).ToList();

            if (offending.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"The '{descriptor.Name}' agent cannot be exposed over {protocol}: the " +
                $"'{string.Join(", ", offending)}' tools ask for user approval. An external " +
                "caller is not a human and cannot answer an approval request. Remove this " +
                "agent from the allow list, or limit it to tools that need no approval.");
        }
    }
}
