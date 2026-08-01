using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism;

/// <summary>
/// Gomulu SQL migration'larini veritabanina uygular.
/// </summary>
/// <remarks>
/// <para>Akis su adimlardan gecer:</para>
/// <list type="number">
///   <item><description><c>pg_advisory_lock</c> alinir — coklu replika ayni anda baslarsa yalnizca biri uygular.</description></item>
///   <item><description>Sema ve <c>__migrations</c> defteri yoksa olusturulur.</description></item>
///   <item><description>Uygulanmis her migration'in ozeti dogrulanir; uyusmazlik <strong>hata verir</strong>.</description></item>
///   <item><description>Uygulanmamis migration'lar sira ile, her biri kendi islemi icinde calistirilir.</description></item>
///   <item><description>Kilit birakilir.</description></item>
/// </list>
/// <para>
/// Kilit oturum kapsamlidir; bu yuzden tum adimlar <em>tek bir baglanti</em>
/// uzerinde yurutulur.
/// </para>
/// </remarks>
public sealed class MigrationRunner
{
    /// <summary>
    /// Migration kilidinin sabit anahtari.
    /// </summary>
    /// <remarks>
    /// Deger AgentPrism'e ozgudur ve <strong>degistirilmemelidir</strong>: eski surumu
    /// calistiran bir replika farkli bir anahtar kullanirsa kilit koruma saglamaz.
    /// </remarks>
    private const long AdvisoryLockKey = 0x41_50_52_49_53_4D_00_01;

    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly AgentPrismPostgreSqlOptions _options;
    private readonly ILogger<MigrationRunner> _logger;

    /// <summary>Yeni bir migration calistiricisi olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public MigrationRunner(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ILogger<MigrationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _dataSource = dataSource;
        _options = options.Value;
        _sql = new SqlQueries(_options.SchemaName);
        _logger = logger;
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
        var migrations = MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            await ExecuteAsync(connection, "SELECT pg_advisory_lock(@key);", command
                => command.Parameters.AddWithValue("key", AdvisoryLockKey), cancellationToken).ConfigureAwait(false);

            try
            {
                return await ApplyPendingAsync(connection, migrations, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Kilit her durumda birakilir. Baglanti kapansa da PostgreSQL kilidi
                // duserdi; acik birakma yine de bekleyen replikalari erken serbest birakir.
                await ExecuteAsync(connection, "SELECT pg_advisory_unlock(@key);", command
                    => command.Parameters.AddWithValue("key", AdvisoryLockKey), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<int> ApplyPendingAsync(
        NpgsqlConnection connection,
        IReadOnlyList<MigrationDescriptor> migrations,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, _sql.CreateSchema, configure: null, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, _sql.CreateMigrationsTable, configure: null, cancellationToken).ConfigureAwait(false);

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
                _sql.Schema);
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
        NpgsqlConnection connection,
        MigrationDescriptor migration,
        CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                var apply = new NpgsqlCommand(_sql.ApplySchema(migration.Sql), connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds,
                };

                await NpgsqlHelpers.ExecuteAsync(apply, cancellationToken).ConfigureAwait(false);

                var record = new NpgsqlCommand(_sql.InsertMigration, connection, transaction)
                {
                    CommandTimeout = _options.CommandTimeoutSeconds,
                };

                record.Parameters.AddWithValue("id", migration.Id);
                record.Parameters.AddWithValue("name", migration.Name);
                record.Parameters.AddWithValue("checksum", migration.Checksum);
                record.Parameters.AddWithValue("applied_at", DateTime.UtcNow);

                await NpgsqlHelpers.ExecuteAsync(record, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                throw new AgentPrismException(
                    $"'{migration.Name}' migration'i uygulanamadi: {ex.MessageText} (SQLSTATE {ex.SqlState}).",
                    ex);
            }
        }
    }

    private async ValueTask<Dictionary<int, AppliedMigration>> ReadAppliedAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(_sql.SelectAppliedMigrations, connection)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };

        var rows = await NpgsqlHelpers.ReadListAsync(
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
        NpgsqlConnection connection,
        string sql,
        Action<NpgsqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeoutSeconds,
        };

        configure?.Invoke(command);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private sealed record AppliedMigration(int Id, string Name, string Checksum);
}
