using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Uygulama baslarken bekleyen migration'lari uygular ve varsayilan kiraci
/// kaydinin var oldugundan emin olur.
/// </summary>
/// <remarks>
/// <para>
/// <c>AutoApplyMigrations</c> kapaliysa migration uygulanmaz. O durumda semanin
/// hazir olmasi tuketicinin sorumlulugundadir; <see cref="MigrationRunner"/> ayri
/// bir dagitim adiminda calistirilabilir.
/// </para>
/// <para>
/// <strong>Hata uygulamayi baslatmaz.</strong> Sema hazir degilken calisan bir
/// AgentPrism sessizce veri kaybeder; bu yuzden migration hatasi yutulmaz.
/// Gozlemlenebilirlik kurali (depo hatasi calistirmayi kesmez) yalnizca
/// calistirma kaydi icindir, sema kurulumu icin degil.
/// </para>
/// <para>
/// Sinif her SQL saglayicisinda ortaktir (Faz 23); saglayiciya ozgu her sey
/// <see cref="SqlStoreContext"/> uzerinden gelir.
/// </para>
/// </remarks>
internal sealed class MigrationHostedService : IHostedService
{
    private readonly MigrationRunner _runner;
    private readonly SqlStoreContext _storeContext;
    private readonly AgentPrismOptions _agentPrismOptions;
    private readonly IEnumerable<SqlPersistenceRegistration> _registrations;
    private readonly ILogger<MigrationHostedService> _logger;

    /// <summary>Yeni bir baslangic servisi olusturur.</summary>
    /// <param name="runner">Migration calistiricisi.</param>
    /// <param name="storeContext">Depo baglami.</param>
    /// <param name="agentPrismOptions">AgentPrism ayarlari.</param>
    /// <param name="registrations">Kayitli kalicilik saglayicilari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public MigrationHostedService(
        MigrationRunner runner,
        SqlStoreContext storeContext,
        IOptions<AgentPrismOptions> agentPrismOptions,
        IEnumerable<SqlPersistenceRegistration> registrations,
        ILogger<MigrationHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(storeContext);
        ArgumentNullException.ThrowIfNull(agentPrismOptions);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _storeContext = storeContext;
        _agentPrismOptions = agentPrismOptions.Value;
        _registrations = registrations;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WarnOnMultipleProviders();

        if (!_storeContext.AutoApplyMigrations)
        {
            _logger.LogInformation(
                "AgentPrism migration'lari otomatik uygulanmiyor (AutoApplyMigrations kapali). " +
                "Semanin guncel olmasi cagiranin sorumlulugundadir.");

            return;
        }

        await _runner.ApplyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDefaultTenantAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Birden fazla kalicilik saglayicisi kayitliysa uyarir.
    /// </summary>
    /// <remarks>
    /// <c>UsePostgreSql()</c> ve <c>UseSqlServer()</c> ayni zincirde cagrilirsa
    /// <em>son kayit kazanir</em> ve verinin hangi veritabanina gittigi cagri
    /// sirasina baglanir. Bu bir yapilandirma hatasidir. Kayit engellenmez —
    /// bilincli bir gecis senaryosu olabilir — ama sessiz kalmaz.
    /// Gerekce: <c>docs/KARARLAR.md</c>, karar K-183.
    /// </remarks>
    private void WarnOnMultipleProviders()
    {
        var names = _registrations.Select(static registration => registration.ProviderName).ToArray();

        if (names.Length <= 1)
        {
            return;
        }

        _logger.LogWarning(
            "AgentPrism'de birden fazla kalicilik saglayicisi kayitli: {Providers}. " +
            "Son kayit kazanir ve su an {Winner} kullaniliyor. Yalnizca birini cagirin.",
            string.Join(", ", names),
            _storeContext.ProviderName);
    }

    private async ValueTask EnsureDefaultTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = _agentPrismOptions.DefaultTenantId;

        var command = _storeContext.CreateCommand(_storeContext.Sql.UpsertTenant);
        DbHelpers.Add(command, "id", AgentPrismId.NewId());
        DbHelpers.Add(command, "slug", tenantId);
        DbHelpers.Add(command, "display_name", tenantId);
        _storeContext.Dialect.AddTimestamp(command, "created_at", DateTimeOffset.UtcNow);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Kayitli bir SQL kalicilik saglayicisinin isareti.
/// </summary>
/// <param name="ProviderName">Saglayici adi. Ornek: <c>PostgreSQL</c>.</param>
/// <remarks>
/// Her <c>Use*</c> uzantisi bir isaret ekler. Isaretler <em>birikir</em>
/// (<c>AddSingleton</c>, <c>TryAdd</c> degil); birden fazlaysa acilista uyari
/// loglanir.
/// </remarks>
internal sealed record SqlPersistenceRegistration(string ProviderName);
