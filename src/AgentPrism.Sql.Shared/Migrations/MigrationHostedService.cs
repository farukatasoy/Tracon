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
    private readonly IEnumerable<SqlPersistenceRegistrationMarker> _registrations;
    private readonly SchemaReadyGate _schemaReadyGate;
    private readonly ILogger<MigrationHostedService> _logger;

    /// <summary>Yeni bir baslangic servisi olusturur.</summary>
    /// <param name="runner">Migration calistiricisi.</param>
    /// <param name="storeContext">Depo baglami.</param>
    /// <param name="agentPrismOptions">AgentPrism ayarlari.</param>
    /// <param name="registrations">Kayitli kalicilik saglayicilari.</param>
    /// <param name="schemaReadyGate">SQL'e dokunan arka plan servislerini bekleten kapi.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public MigrationHostedService(
        MigrationRunner runner,
        SqlStoreContext storeContext,
        IOptions<AgentPrismOptions> agentPrismOptions,
        IEnumerable<SqlPersistenceRegistrationMarker> registrations,
        SchemaReadyGate schemaReadyGate,
        ILogger<MigrationHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(storeContext);
        ArgumentNullException.ThrowIfNull(agentPrismOptions);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _storeContext = storeContext;
        _agentPrismOptions = agentPrismOptions.Value;
        _registrations = registrations;
        _schemaReadyGate = schemaReadyGate;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsWinningProvider())
        {
            // 🚨 Birden fazla kalicilik saglayicisi kayitliysa BURADAN once
            // WarnOnMultipleProviders() zaten uyarmis olur. Sql.Shared her
            // saglayicida AYRI derlenir (bu dosya link ile kopyalanir, K-176) —
            // yani SqlStoreContext/MigrationRunner/MigrationHostedService HER
            // saglayicida FARKLI bir CLR tipidir. `services.Replace(...)` bu
            // yuzden yalnizca AYNI saglayicinin kendi tipini degistirir; rakip
            // saglayicinin kaydini SILMEZ. Bu koruma olmadan kaybeden saglayici
            // de kendi semasini/varsayilan kiracisini SESSIZCE yazardi (olculdu:
            // MT-PKG-082, iki veritabaninda da sema olustu). Yalnizca kazanan
            // (son kaydedilen) saglayici migration uygular; digerleri kapiyi
            // acar ve hicbir seye dokunmadan cikar.
            WarnOnMultipleProviders();
            _schemaReadyGate.MarkReady();

            return;
        }

        WarnOnMultipleProviders();

        if (!_storeContext.AutoApplyMigrations)
        {
            _logger.LogInformation(
                "AgentPrism migration'lari otomatik uygulanmiyor (AutoApplyMigrations kapali). " +
                "Semanin guncel olmasi cagiranin sorumlulugundadir.");

            // Kapi ACILIR: sema tuketicinin sorumlulugundadir ve arka plan
            // servislerini sonsuza dek beklemekte tutmanin faydasi yoktur.
            _schemaReadyGate.MarkReady();

            return;
        }

        await _runner.ApplyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDefaultTenantAsync(cancellationToken).ConfigureAwait(false);

        // 🚨 Kapi yalnizca BURADA, migration ve varsayilan kiraci yaziminin
        // ikisi de bittikten sonra acilir. Migration hata verirse kapi kapali
        // kalir; barindirici zaten baslamaz ve bekleyen servisler
        // stoppingToken uzerinden cikar. Gerekce: K-354.
        _schemaReadyGate.MarkReady();
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
            WinningProviderName());
    }

    /// <summary>
    /// Bu ornegin baglandigi saglayici, "son kayit kazanir" kuralina gore
    /// kazanan mi.
    /// </summary>
    /// <remarks>
    /// <see cref="_registrations"/> paylasilan (<c>AgentPrism.Abstractions</c>)
    /// bir tip oldugu icin TUM saglayicilardan gelen isaretleri gorur ve kayit
    /// sirasini korur; son eleman "son cagrilan" saglayicidir. <see cref="_storeContext"/>
    /// ise bu derlemeye OZGUDUR (Sql.Shared ayri derlenir) — yalniz kendi adini
    /// paylasilan listedeki son adla karsilastirarak "kazanan miyim" sorusunu
    /// yanitlayabilir.
    /// </remarks>
    private bool IsWinningProvider()
    {
        var names = _registrations.Select(static registration => registration.ProviderName).ToArray();

        return names.Length == 0
            || string.Equals(names[^1], _storeContext.ProviderName, StringComparison.Ordinal);
    }

    private string WinningProviderName()
        => _registrations.Select(static registration => registration.ProviderName).LastOrDefault()
            ?? _storeContext.ProviderName;

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
