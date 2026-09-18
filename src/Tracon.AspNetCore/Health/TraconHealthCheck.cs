using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tracon;

/// <summary>
/// Check that connects the Tracon setup to the standard.NET health check system.
/// </summary>
/// <remarks>
/// <para>
/// Reads the report collected by <see cref="TraconDiagnosticsCollector"/> — it
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
/// circuit is open, more than one persistence provider is registered, no model
/// provider is registered at all, or every provider that WAS probed came back
/// unhealthy.
/// </description></item>
/// <item>
/// <description><see cref="HealthStatus.Healthy"/>: the database is reachable, no migration is pending, and no model provider is known to be broken.</description>
/// </item>
/// </list>
/// <para>
/// 🚨 "Not probed yet" is not a degradation, and the two used to be one branch.
/// Nothing probes a provider on its own: the cache fills only when
/// <c>GET /api/models/health</c> is called or when
/// <c>Tracon:Health:BackgroundInterval</c> is set, and a real chat call does
/// not write to it either. A correctly installed application — database
/// reachable, every migration applied, every provider key resolved — therefore
/// reported <see cref="HealthStatus.Degraded"/> forever, and an operator
/// watching it could not tell a genuine outage from the resting state. The
/// absence of a measurement is now reported as what it is, in the message,
/// while the status reflects only what is actually known to be wrong. The
/// probe itself is still never triggered from here: this check has no side
/// effects, which is why it can be polled by an orchestrator.
/// </para>
/// </remarks>
internal sealed class TraconHealthCheck(TraconDiagnosticsCollector collector) : IHealthCheck
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

        if (report.ModelProviders.Count == 0)
        {
            return HealthCheckResult.Degraded(
                "No model provider is registered; no agent can run. Call a Use*() provider method.",
                data: data);
        }

        if (report.ModelProviders.Any(
                static provider => string.Equals(
                    provider.Status, nameof(ModelProviderHealthStatus.Healthy), StringComparison.Ordinal)))
        {
            return HealthCheckResult.Healthy("The setup is healthy.", data: data);
        }

        // Unknown covers both "never probed" and "this provider ships no health
        // check at all"; neither is evidence of a fault, and neither should
        // colour the indicator.
        var probed = report.ModelProviders
            .Where(static provider => !string.Equals(
                provider.Status, nameof(ModelProviderHealthStatus.Unknown), StringComparison.Ordinal))
            .ToArray();

        if (probed.Length == 0)
        {
            return HealthCheckResult.Healthy(
                "The setup is healthy. No model provider has been probed yet — a probe runs when " +
                "GET /api/models/health is called, or on the interval set by Tracon:Health:BackgroundInterval.",
                data: data);
        }

        return HealthCheckResult.Degraded(
            "No model provider is healthy: " +
            string.Join(", ", probed.Select(static provider => $"{provider.Name} is {provider.Status}")) + ".",
            data: data);
    }
}
