using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentPrism;

/// <summary>
/// Check that connects the AgentPrism setup to the standard.NET health check system.
/// </summary>
/// <remarks>
/// <para>
/// Reads the report collected by <see cref="AgentPrismDiagnosticsCollector"/> — it
/// <strong>produces</strong> no model call or extra database query, other than the
/// active SQL provider's lightweight connection probe (see <c>ISqlPersistenceDiagnostics</c>).
/// </para>
/// <para>Three states.</para>
/// <list type="bullet">
/// <item>
/// <description><see cref="HealthStatus.Unhealthy"/>: the database is unreachable, or a migration is pending.</description>
/// </item>
/// <item><description>
/// <see cref="HealthStatus.Degraded"/>: the database is reachable, but a model provider's
/// circuit is open, more than one persistence provider is registered, or no model
/// provider has yet been confirmed healthy.
/// </description></item>
/// <item>
/// <description><see cref="HealthStatus.Healthy"/>: the database is reachable, no migration is pending, and at least one model provider is healthy.</description>
/// </item>
/// </list>
/// </remarks>
internal sealed class AgentPrismHealthCheck(AgentPrismDiagnosticsCollector collector) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var report = await collector.CollectAsync(cancellationToken).ConfigureAwait(false);

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["persistenceProvider"] = report.PersistenceProvider,
            ["registeredPersistenceProviders"] = report.RegisteredPersistenceProviders,
            ["migrationsUpToDate"] = report.MigrationsUpToDate,
        };

        if (!report.CanConnect)
        {
            return HealthCheckResult.Unhealthy("The persistence database is unreachable.", data: data);
        }

        if (!report.MigrationsUpToDate)
        {
            return HealthCheckResult.Unhealthy(
                $"{report.PendingMigrations.Count} migration(s) are pending.",
                data: data);
        }

        if (report.RegisteredPersistenceProviders > 1)
        {
            return HealthCheckResult.Degraded(
                $"More than one persistence provider is registered ({report.RegisteredPersistenceProviders}); " +
                $"'{report.PersistenceProvider}' currently wins. Call only one Use*().",
                data: data);
        }

        var openCircuits = report.ModelProviders.Where(static provider => provider.CircuitOpen).ToArray();

        if (openCircuits.Length > 0)
        {
            return HealthCheckResult.Degraded(
                $"Circuit breaker open: {string.Join(", ", openCircuits.Select(static provider => provider.Name))}.",
                data: data);
        }

        var hasHealthyProvider = report.ModelProviders.Any(
            static provider => string.Equals(provider.Status, nameof(ModelProviderHealthStatus.Healthy), StringComparison.Ordinal));

        return hasHealthyProvider
            ? HealthCheckResult.Healthy("The setup is healthy.", data: data)
            : HealthCheckResult.Degraded(
                "No model provider has been confirmed healthy yet.",
                data: data);
    }
}
