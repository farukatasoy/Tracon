using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism;

/// <summary>
/// Uygulama baslarken bekleyen migration'lari uygular ve varsayilan kiraci
/// kaydinin var oldugundan emin olur.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentPrismPostgreSqlOptions.AutoApplyMigrations"/> kapaliysa hicbir
/// sey yapmaz. O durumda semanin hazir olmasi tuketicinin sorumlulugundadir;
/// <see cref="MigrationRunner"/> ayri bir dagitim adiminda calistirilabilir.
/// </para>
/// <para>
/// <strong>Hata uygulamayi baslatmaz.</strong> Sema hazir degilken calisan bir
/// AgentPrism sessizce veri kaybeder; bu yuzden migration hatasi yutulmaz.
/// Gozlemlenebilirlik kurali (depo hatasi calistirmayi kesmez) yalnizca
/// calistirma kaydi icindir, sema kurulumu icin degil.
/// </para>
/// </remarks>
public sealed class MigrationHostedService : IHostedService
{
    private readonly MigrationRunner _runner;
    private readonly NpgsqlDataSource _dataSource;
    private readonly AgentPrismPostgreSqlOptions _postgreSqlOptions;
    private readonly AgentPrismOptions _agentPrismOptions;
    private readonly ILogger<MigrationHostedService> _logger;

    /// <summary>Yeni bir baslangic servisi olusturur.</summary>
    /// <param name="runner">Migration calistiricisi.</param>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="postgreSqlOptions">PostgreSQL ayarlari.</param>
    /// <param name="agentPrismOptions">AgentPrism ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public MigrationHostedService(
        MigrationRunner runner,
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> postgreSqlOptions,
        IOptions<AgentPrismOptions> agentPrismOptions,
        ILogger<MigrationHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(postgreSqlOptions);
        ArgumentNullException.ThrowIfNull(agentPrismOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _runner = runner;
        _dataSource = dataSource;
        _postgreSqlOptions = postgreSqlOptions.Value;
        _agentPrismOptions = agentPrismOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_postgreSqlOptions.AutoApplyMigrations)
        {
            _logger.LogInformation(
                "AgentPrism migration'lari otomatik uygulanmiyor ({Option} kapali). " +
                "Semanin guncel olmasi cagiranin sorumlulugundadir.",
                nameof(AgentPrismPostgreSqlOptions.AutoApplyMigrations));

            return;
        }

        await _runner.ApplyAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDefaultTenantAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async ValueTask EnsureDefaultTenantAsync(CancellationToken cancellationToken)
    {
        var queries = new SqlQueries(_postgreSqlOptions.SchemaName);
        var tenantId = _agentPrismOptions.DefaultTenantId;

        var command = _dataSource.CreateCommand(queries.UpsertTenant);
        command.CommandTimeout = _postgreSqlOptions.CommandTimeoutSeconds;
        command.Parameters.AddWithValue("id", AgentPrismId.NewId());
        command.Parameters.AddWithValue("slug", tenantId);
        command.Parameters.AddWithValue("display_name", tenantId);
        command.Parameters.AddWithValue("created_at", DateTime.UtcNow);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
