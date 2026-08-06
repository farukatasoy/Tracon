using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Gomulu SQL migration'larini veritabanina uygular.
/// </summary>
/// <remarks>
/// <para>Akis su adimlardan gecer:</para>
/// <list type="number">
///   <item><description>Saglayiciya ozgu migration kilidi alinir — coklu replika ayni anda baslarsa yalnizca biri uygular.</description></item>
///   <item><description>Sema ve <c>__migrations</c> defteri yoksa olusturulur.</description></item>
///   <item><description>Uygulanmis her migration'in ozeti dogrulanir; uyusmazlik <strong>hata verir</strong>.</description></item>
///   <item><description>Uygulanmamis migration'lar sira ile, her biri kendi islemi icinde calistirilir.</description></item>
///   <item><description>Kilit birakilir.</description></item>
/// </list>
/// <para>
/// Kilit oturum kapsamlidir; bu yuzden tum adimlar <em>tek bir baglanti</em>
/// uzerinde yurutulur.
/// </para>
/// <para>
/// Sinif her SQL saglayicisinda ortaktir; kilit bicimi (<c>pg_advisory_lock</c>
/// / <c>sp_getapplock</c>) ve migration kaynak oneki <see cref="SqlDialect"/>
/// uzerinden gelir.
/// </para>
/// </remarks>
public sealed class MigrationRunner : ISqlPersistenceDiagnostics
{
    private readonly SqlStoreContext _context;
    private readonly ILogger<MigrationRunner> _logger;

    /// <summary>Yeni bir migration calistiricisi olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    internal MigrationRunner(SqlStoreContext context, ILogger<MigrationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => _context.ProviderName;

    /// <summary>
    /// Baglanti ve migration durumunu, hicbir migration UYGULAMADAN okur (Faz 33).
    /// </summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Baglanti kurulamadiysa <c>CanConnect: false</c>; kurulduysa bekleyen migration listesi.</returns>
    public async ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var dialect = _context.Dialect;

        var migrations = MigrationDescriptor.Discover(
            dialect.GetType().Assembly,
            dialect.MigrationResourcePrefix);

        DbConnection connection;

        try
        {
            connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException)
        {
            return new SqlPersistenceDiagnosticsSnapshot { CanConnect = false, PendingMigrations = [] };
        }

        await using (connection.ConfigureAwait(false))
        {
            try
            {
                var applied = await ReadAppliedAsync(connection, cancellationToken).ConfigureAwait(false);

                var pending = migrations
                    .Where(migration => !applied.ContainsKey(migration.Id))
                    .Select(static migration => migration.Name)
                    .ToArray();

                return new SqlPersistenceDiagnosticsSnapshot { CanConnect = true, PendingMigrations = pending };
            }
            catch (DbException)
            {
                // __migrations defteri henuz yok (ilk migration hic uygulanmamis).
                // Baglanti calisiyor; tum migration'lar bekliyor sayilir.
                return new SqlPersistenceDiagnosticsSnapshot
                {
                    CanConnect = true,
                    PendingMigrations = migrations.Select(static migration => migration.Name).ToArray(),
                };
            }
        }
    }

    /// <summary>Bekleyen migration'lari uygular.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Uygulanan migration sayisi. Her sey guncelse 0.</returns>
    /// <exception cref="AgentPrismException">
    /// Uygulanmis bir migration dosyasi degistirilmisse (ozet uyusmazligi) veya
    /// bir migration calistirilirken hata olustuysa.
    /// </exception>
    public async ValueTask<int> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var dialect = _context.Dialect;

        var migrations = MigrationDescriptor.Discover(
            dialect.GetType().Assembly,
            dialect.MigrationResourcePrefix);

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            await dialect.AcquireMigrationLockAsync(
                connection,
                _context.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            try
            {
                return await ApplyPendingAsync(connection, migrations, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Kilit her durumda birakilir. Baglanti kapansa da kilit duserdi;
                // acik birakma yine de bekleyen replikalari erken serbest birakir.
                await dialect.ReleaseMigrationLockAsync(
                    connection,
                    _context.CommandTimeoutSeconds,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<int> ApplyPendingAsync(
        DbConnection connection,
        IReadOnlyList<MigrationDescriptor> migrations,
        CancellationToken cancellationToken)
    {
        var sql = _context.Sql;

        await ExecuteAsync(connection, sql.CreateSchema, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, sql.CreateMigrationsTable, cancellationToken).ConfigureAwait(false);

        var applied = await ReadAppliedAsync(connection, cancellationToken).ConfigureAwait(false);
        var count = 0;

        foreach (var migration in migrations)
        {
            if (applied.TryGetValue(migration.Id, out var record))
            {
                VerifyChecksum(migration, record);
                continue;
            }

            await ApplyOneAsync(connection, migration, cancellationToken).ConfigureAwait(false);
            count++;
        }

        if (count > 0)
        {
            _logger.LogInformation(
                "AgentPrism {Count} migration uyguladi. Sema: {Schema}.",
                count,
                sql.Schema);
        }

        return count;
    }

    private static void VerifyChecksum(MigrationDescriptor migration, AppliedMigration record)
    {
        if (string.Equals(record.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new AgentPrismException(
            $"'{migration.Name}' migration'i veritabaninda uygulanmis ancak dosyanin icerigi degismis. " +
            $"Veritabanindaki ozet: {record.Checksum}, dosyanin ozeti: {migration.Checksum}. " +
            "Uygulanmis bir migration duzenlenmez; degisiklik icin yeni bir migration dosyasi ekleyin.");
    }

    private async ValueTask ApplyOneAsync(
        DbConnection connection,
        MigrationDescriptor migration,
        CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                var apply = _context.CreateCommand(
                    _context.Sql.ApplySchema(migration.Sql),
                    connection,
                    transaction);

                await DbHelpers.ExecuteAsync(apply, cancellationToken).ConfigureAwait(false);

                var record = _context.CreateCommand(_context.Sql.InsertMigration, connection, transaction);
                var dialect = _context.Dialect;

                dialect.AddInt32(record, "id", migration.Id);
                dialect.AddText(record, "name", migration.Name);
                dialect.AddText(record, "checksum", migration.Checksum);
                dialect.AddTimestamp(record, "applied_at", DateTimeOffset.UtcNow);

                await DbHelpers.ExecuteAsync(record, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbException ex) when (_context.Dialect.DescribeDatabaseError(ex) is { } description)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                throw new AgentPrismException(
                    $"'{migration.Name}' migration'i uygulanamadi: {description}.",
                    ex);
            }
        }
    }

    private async ValueTask<Dictionary<int, AppliedMigration>> ReadAppliedAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_context.Sql.SelectAppliedMigrations, connection, transaction: null);

        var rows = await DbHelpers.ReadListAsync(
            command,
            static reader => new AppliedMigration(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)),
            cancellationToken).ConfigureAwait(false);

        var applied = new Dictionary<int, AppliedMigration>(rows.Count);

        foreach (var row in rows)
        {
            applied[row.Id] = row;
        }

        return applied;
    }

    private async ValueTask ExecuteAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(sql, connection, transaction: null);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private sealed record AppliedMigration(int Id, string Name, string Checksum);
}
