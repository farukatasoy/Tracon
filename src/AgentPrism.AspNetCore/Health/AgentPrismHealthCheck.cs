using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentPrism;

/// <summary>
/// AgentPrism kurulumunu standart .NET saglik denetim sistemine baglayan denetim (Faz 33).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentPrismDiagnosticsCollector"/>'un topladigi raporu okur — hicbir
/// model cagrisi veya ek veritabani sorgusu <strong>uretmez</strong> disinda etkin SQL
/// saglayicisinin hafif baglanti sinamasi (bkz. <c>ISqlPersistenceDiagnostics</c>).
/// </para>
/// <para>Uc durum. Gerekce: <c>docs/33-SAGLIK-DENETIMI-VE-TESHIS.md</c>, bolum 33.3.</para>
/// <list type="bullet">
/// <item><description><see cref="HealthStatus.Unhealthy"/>: veritabanina erisilemiyor veya bekleyen migration var.</description></item>
/// <item><description>
/// <see cref="HealthStatus.Degraded"/>: veritabani erisilebilir ama bir model saglayicisinin
/// devresi acik, birden fazla kalicilik saglayicisi kayitli (K-183) veya hicbir model
/// saglayicisi henuz saglikli olarak dogrulanmadi.
/// </description></item>
/// <item><description><see cref="HealthStatus.Healthy"/>: veritabani erisilebilir, bekleyen migration yok, en az bir model saglayicisi saglikli.</description></item>
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
            return HealthCheckResult.Unhealthy("Kalicilik veritabanina erisilemiyor.", data: data);
        }

        if (!report.MigrationsUpToDate)
        {
            return HealthCheckResult.Unhealthy(
                $"{report.PendingMigrations.Count} bekleyen migration var.",
                data: data);
        }

        if (report.RegisteredPersistenceProviders > 1)
        {
            return HealthCheckResult.Degraded(
                $"Birden fazla kalicilik saglayicisi kayitli ({report.RegisteredPersistenceProviders}); " +
                $"su an '{report.PersistenceProvider}' kazaniyor. Yalniz bir Use*() cagirin.",
                data: data);
        }

        var openCircuits = report.ModelProviders.Where(static provider => provider.CircuitOpen).ToArray();

        if (openCircuits.Length > 0)
        {
            return HealthCheckResult.Degraded(
                $"Devre kesici acik: {string.Join(", ", openCircuits.Select(static provider => provider.Name))}.",
                data: data);
        }

        var hasHealthyProvider = report.ModelProviders.Any(
            static provider => string.Equals(provider.Status, nameof(ModelProviderHealthStatus.Healthy), StringComparison.Ordinal));

        return hasHealthyProvider
            ? HealthCheckResult.Healthy("Kurulum saglikli.", data: data)
            : HealthCheckResult.Degraded(
                "Henuz saglikli oldugu dogrulanmis bir model saglayicisi yok.",
                data: data);
    }
}
