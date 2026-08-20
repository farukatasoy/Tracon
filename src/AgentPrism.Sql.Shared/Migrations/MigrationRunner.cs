using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Applies the embedded SQL migrations to the database.
/// </summary>
/// <remarks>
/// <para>The flow goes through these steps:</para>
/// <list type="number">
/// <item>
/// <description>The provider-specific migration lock is acquired — it is SCOPED to the schema: if multiple replicas start against the same schema at the same time, only one applies.</description>
/// </item>
///   <item><description>The schema and the <c>__migrations</c> ledger are created if missing.</description></item>
///   <item><description>Every applied migration's checksum is verified; a mismatch <strong>fails</strong>.</description></item>
///   <item><description>Unapplied migrations run in order, each inside its own transaction.</description></item>
///   <item><description>The lock is released.</description></item>
/// </list>
/// <para>
/// The lock is session-scoped, so all steps run over <em>a single connection</em>.
/// </para>
/// <para>
/// This class is shared across every SQL provider; the lock mechanism
/// (<c>pg_advisory_lock</c> / <c>sp_getapplock</c>) and the migration resource
/// prefix come through <see cref="SqlDialect"/>.
/// </para>
/// <para>
/// Even though the lock is scoped to the schema, some migrations may create
/// a catalog object shared across the WHOLE database (for example PostgreSQL's
/// <c>CREATE EXTENSION IF NOT EXISTS</c>). If two different schemas migrate for
/// the first time concurrently, such "IF NOT EXISTS"-guarded DDL can still hit
/// a uniqueness violation; <see cref="ApplyOneAsync"/> retries this safely.
/// </para>
/// </remarks>
public sealed class MigrationRunner : ISqlPersistenceDiagnostics
{
    private readonly SqlStoreContext _context;
    private readonly ILogger<MigrationRunner> _logger;

    /// <summary>Creates a new migration runner.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    internal MigrationRunner(SqlStoreContext context, ILogger<MigrationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => _context.ProviderName;

    /// <summary>The name of the set every provider always applies.</summary>
    private const string CoreSetName = "core";

    /// <summary>
    /// Reads the connection and migration status WITHOUT applying any migration.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>CanConnect: false</c> if the connection could not be established; otherwise the list of pending migrations.
    /// </returns>
    public async ValueTask<SqlPersistenceDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var sets = DiscoverActiveSets();

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

                var pending = EnumeratePending(sets, applied).ToArray();

                return new SqlPersistenceDiagnosticsSnapshot { CanConnect = true, PendingMigrations = pending };
            }
            catch (DbException)
            {
                // The __migrations ledger does not exist yet (no migration has
                // ever been applied). The connection works; every migration
                // counts as pending.
                return new SqlPersistenceDiagnosticsSnapshot
                {
                    CanConnect = true,
                    PendingMigrations = EnumeratePending(sets, new Dictionary<MigrationKey, AppliedMigration>()).ToArray(),
                };
            }
        }
    }

    /// <summary>Applies pending migrations.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of applied migrations. 0 if everything is current.</returns>
    /// <exception cref="AgentPrismException">
    /// An applied migration file has been modified (checksum mismatch), an
    /// unknown migration set is enabled, or an error occurred while running a
    /// migration.
    /// </exception>
    public async ValueTask<int> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var sets = DiscoverActiveSets();

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            await _context.Dialect.AcquireMigrationLockAsync(
                connection,
                _context.CommandTimeoutSeconds,
                cancellationToken).ConfigureAwait(false);

            try
            {
                return await ApplyPendingAsync(connection, sets, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // The lock is released in every case. It would also drop if the
                // connection closed; releasing it explicitly still frees waiting
                // replicas earlier.
                await _context.Dialect.ReleaseMigrationLockAsync(
                    connection,
                    _context.CommandTimeoutSeconds,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Discovers the core set plus every enabled optional set.
    /// </summary>
    /// <exception cref="AgentPrismException">
    /// <see cref="SqlStoreContext.EnabledMigrationSets"/> names a set this
    /// provider does not offer.
    /// </exception>
    private List<MigrationSet> DiscoverActiveSets()
    {
        var dialect = _context.Dialect;
        var assembly = dialect.GetType().Assembly;

        var sets = new List<MigrationSet>
        {
            new(CoreSetName, MigrationDescriptor.Discover(assembly, dialect.MigrationResourcePrefix)),
        };

        foreach (var setName in _context.EnabledMigrationSets)
        {
            if (!dialect.OptionalMigrationResourcePrefixes.TryGetValue(setName, out var prefix))
            {
                var known = string.Join(", ", dialect.OptionalMigrationResourcePrefixes.Keys);

                throw new AgentPrismException(
                    $"Unknown migration set '{setName}' for provider '{_context.ProviderName}'. " +
                    (known.Length == 0
                        ? "This provider offers no optional migration sets."
                        : $"Known optional sets: {known}."));
            }

            sets.Add(new MigrationSet(setName, MigrationDescriptor.Discover(assembly, prefix)));
        }

        return sets;
    }

    /// <summary>
    /// Formats the still-pending migrations of every set. A non-core set's
    /// entries are prefixed with the set name (<c>"knowledge:0001_vector"</c>)
    /// so two sets numbering from <c>0001</c> stay distinguishable; the core
    /// set keeps its bare name for backward compatibility.
    /// </summary>
    private static IEnumerable<string> EnumeratePending(
        IReadOnlyList<MigrationSet> sets,
        IReadOnlyDictionary<MigrationKey, AppliedMigration> applied)
    {
        foreach (var set in sets)
        {
            foreach (var migration in set.Migrations)
            {
                if (applied.ContainsKey(new MigrationKey(set.Name, migration.Id)))
                {
                    continue;
                }

                yield return string.Equals(set.Name, CoreSetName, StringComparison.Ordinal)
                    ? migration.Name
                    : $"{set.Name}:{migration.Name}";
            }
        }
    }

    private async ValueTask<int> ApplyPendingAsync(
        DbConnection connection,
        IReadOnlyList<MigrationSet> sets,
        CancellationToken cancellationToken)
    {
        var sql = _context.Sql;

        await ExecuteAsync(connection, sql.CreateSchema, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, sql.CreateMigrationsTable, cancellationToken).ConfigureAwait(false);

        // Runs as its OWN command, completed before any InsertMigration text
        // is compiled — see the 🚨 on SqlDialect.UpgradeMigrationsTableAsync.
        await _context.Dialect.UpgradeMigrationsTableAsync(
            connection,
            _context.CommandTimeoutSeconds,
            cancellationToken).ConfigureAwait(false);

        var applied = await ReadAppliedAsync(connection, cancellationToken).ConfigureAwait(false);
        var count = 0;

        foreach (var set in sets)
        {
            foreach (var migration in set.Migrations)
            {
                var key = new MigrationKey(set.Name, migration.Id);

                if (applied.TryGetValue(key, out var record))
                {
                    VerifyChecksum(migration, record);
                    continue;
                }

                await ApplyOneAsync(connection, set.Name, migration, cancellationToken).ConfigureAwait(false);
                count++;
            }
        }

        LogOrphanedRelocatedMigrations(applied);

        if (count > 0)
        {
            _logger.LogInformation(
                "AgentPrism applied {Count} migration(s). Schema: {Schema}.",
                count,
                sql.Schema);
        }

        return count;
    }

    /// <summary>
    /// Logs (once, at information level) a core-numbered ledger row whose
    /// migration was later relocated to an optional set that is not enabled
    /// (decision 67.3). The row and whatever it created are
    /// harmless — this is a diagnostic, not a migration.
    /// </summary>
    private void LogOrphanedRelocatedMigrations(IReadOnlyDictionary<MigrationKey, AppliedMigration> applied)
    {
        foreach (var (id, setName) in _context.Dialect.RelocatedCoreMigrationSets)
        {
            if (!applied.ContainsKey(new MigrationKey(CoreSetName, id)))
            {
                continue;
            }

            if (_context.EnabledMigrationSets.Contains(setName))
            {
                continue;
            }

            _logger.LogInformation(
                "AgentPrism's migration ledger has a core-numbered entry (id {Id}) whose file was later " +
                "relocated to the optional '{Set}' migration set, which is not enabled here. The row and " +
                "whatever it created remain in place and are harmless; enable the set if the feature is " +
                "still wanted, or ignore this message otherwise.",
                id,
                setName);
        }
    }

    private static void VerifyChecksum(MigrationDescriptor migration, AppliedMigration record)
    {
        if (string.Equals(record.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new AgentPrismException(
            $"Migration '{migration.Name}' has been applied to the database but the file's content has changed. " +
            $"Checksum in the database: {record.Checksum}, checksum of the file: {migration.Checksum}. " +
            "An applied migration is never edited; add a new migration file for the change.");
    }

    /// <summary>
    /// The number of retries when a migration races another schema's
    /// concurrent migration over a catalog object shared across the whole
    /// database (for example PostgreSQL's <c>CREATE EXTENSION</c>) and hits a
    /// uniqueness violation.
    /// </summary>
    private const int UniqueViolationRetryAttempts = 8;

    private async ValueTask ApplyOneAsync(
        DbConnection connection,
        string setName,
        MigrationDescriptor migration,
        CancellationToken cancellationToken)
    {
        // The migration's own SQL and the INSERT that writes to the ledger are
        // sent together in a SINGLE round trip (K-388). The __migrations table
        // already exists BEFORE the migration runs and does not reference any
        // object created/changed by the migration's DDL; so it does not fall
        // into K-318's "reference to an object changed in the same batch" trap.
        var sql = ApplyTemplate(_context.Sql.ApplySchema(migration.Sql))
            + Environment.NewLine
            + _context.Sql.InsertMigration;

        for (var attempt = 1; attempt <= UniqueViolationRetryAttempts; attempt++)
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    var command = _context.CreateCommand(sql, connection, transaction);
                    var dialect = _context.Dialect;

                    dialect.AddText(command, "set_name", setName);
                    dialect.AddInt32(command, "id", migration.Id);
                    dialect.AddText(command, "name", migration.Name);
                    dialect.AddText(command, "checksum", migration.Checksum);
                    dialect.AddTimestamp(command, "applied_at", DateTimeOffset.UtcNow);

                    await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return;
                }
                catch (DbException ex) when (
                    _context.Dialect.IsUniqueViolation(ex) && attempt < UniqueViolationRetryAttempts)
                {
                    // The migration lock is SCOPED TO THE SCHEMA (K-389): if a
                    // catalog object shared across the whole database (for
                    // example a PostgreSQL extension) is created at the same
                    // time by another schema's migration, this hits a
                    // uniqueness violation. Migrations write ONLY "IF NOT EXISTS"-
                    // guarded DDL, so retrying is safe — the next attempt finds
                    // the guarded object already exists and skips it. 🚨 A
                    // random delay is required: when dozens of schema fixtures
                    // migrate for the first time at once, a fixed delay would
                    // make them all retry at the same instant and would not
                    // spread out the crowd (herd avoidance).
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(10, 40) * attempt), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (DbException ex) when (_context.Dialect.DescribeDatabaseError(ex) is { } description)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                    throw new AgentPrismException(
                        $"Migration '{migration.Name}' could not be applied: {description}.",
                        ex);
                }
            }
        }
    }

    /// <summary>
    /// Substitutes every key in <see cref="SqlStoreContext.MigrationTemplateValues"/>
    /// for its <c>{key}</c> placeholder (for example <c>{dimension}</c>).
    /// </summary>
    private string ApplyTemplate(string sql)
    {
        if (_context.MigrationTemplateValues.Count == 0)
        {
            return sql;
        }

        foreach (var (key, value) in _context.MigrationTemplateValues)
        {
            sql = sql.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return sql;
    }

    private async ValueTask<Dictionary<MigrationKey, AppliedMigration>> ReadAppliedAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_context.Sql.SelectAppliedMigrations, connection, transaction: null);

        var rows = await DbHelpers.ReadListAsync(
            command,
            static reader => new AppliedMigration(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3)),
            cancellationToken).ConfigureAwait(false);

        var applied = new Dictionary<MigrationKey, AppliedMigration>(rows.Count);

        foreach (var row in rows)
        {
            applied[new MigrationKey(row.SetName, row.Id)] = row;
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

    private sealed record AppliedMigration(string SetName, int Id, string Name, string Checksum);

    /// <summary>A discovered migration set: its name and the migrations found under its resource prefix.</summary>
    private sealed record MigrationSet(string Name, IReadOnlyList<MigrationDescriptor> Migrations);

    /// <summary>The ledger lookup key: a migration set name plus its within-set sequence number.</summary>
    private readonly record struct MigrationKey(string SetName, int Id);
}
