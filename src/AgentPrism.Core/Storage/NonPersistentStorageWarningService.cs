using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Warns once, while the host starts, when a Production installation keeps its
/// data only in process memory.
/// </summary>
/// <remarks>
/// <para>
/// It never throws. Reading which store is registered runs a consumer's own code
/// when a store is wrapped in a custom <see cref="IAuditDecorated"/>, and a
/// failure there is logged at debug level instead of stopping the host.
/// </para>
/// <para>
/// In-memory storage is a supported mode, not a fallback for a broken setup —
/// it is also the default a consumer gets before they register a persistence
/// package. That is why this is a warning and never an exception: failing
/// startup would break a deliberate demo or test installation.
/// </para>
/// <para>
/// The check reads no database and opens no connection. It only looks at which
/// store implementations the container resolved, so it is independent of
/// migration order and it cannot stop a host from starting.
/// </para>
/// <para>
/// <paramref name="environment"/> is optional because a consumer can call
/// <c>AddAgentPrism()</c> on a bare service collection that no host ever backs;
/// <see cref="IHostEnvironment"/> is registered by the host, not by AgentPrism.
/// Without it the environment is unknown, so the service stays silent rather
/// than guessing. It is registered through an explicit factory for the same
/// reason: the built-in container treats an unregistered constructor dependency
/// as required even when the parameter has a default value.
/// </para>
/// </remarks>
/// <param name="environment">The host environment, when a host provides one.</param>
/// <param name="definitions">The registered agent definition store.</param>
/// <param name="runs">The registered run store.</param>
/// <param name="sessions">The registered session store.</param>
/// <param name="logger">The logger the warning is written to.</param>
internal sealed class NonPersistentStorageWarningService(
    IHostEnvironment? environment,
    IAgentDefinitionStore definitions,
    IRunStore runs,
    ISessionStore sessions,
    ILogger<NonPersistentStorageWarningService> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (environment is null || !environment.IsProduction())
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<string> nonPersistent;

        try
        {
            nonPersistent = StorePersistence.NonPersistentStores(definitions, runs, sessions);
        }
        catch (Exception exception)
        {
            // IAuditDecorated is a public contract, so AuditedInner runs a
            // consumer's code. Reporting the storage mode must never be the
            // reason a host fails to start.
            logger.LogDebug(
                exception,
                "The storage persistence check could not read the registered stores. No warning is reported.");

            return Task.CompletedTask;
        }

        if (nonPersistent.Count == 0)
        {
            return Task.CompletedTask;
        }

        logger.LogWarning(
            "AgentPrism is running in the Production environment with storage that is not " +
            "persistent: {NonPersistentStores}. The data lives as long as this process does; " +
            "a restart loses it and a second instance does not see it. Register a persistence " +
            "package to keep it - for example UsePostgreSql(connectionString), UseSqlServer(...) " +
            "or UseSqlite(...). In-memory storage is a supported mode; this message reports the " +
            "environment, it does not report a broken setup.",
            string.Join(", ", nonPersistent));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
