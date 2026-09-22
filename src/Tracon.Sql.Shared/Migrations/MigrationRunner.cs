using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
internal sealed class MigrationRunner : ISqlPersistenceDiagnostics, IMigrationApplier
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
    /// <exception cref="TraconException">
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
    /// <exception cref="TraconException">
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

                throw new TraconException(
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

        // 🚨 EVERY bootstrap step runs under the same transient-conflict retry as
        // a migration does. They are not "setup that cannot fail": the migration
        // lock is scoped to the SCHEMA (K-389), so two schemas bootstrapping at
        // the same time meet on catalog objects shared by the whole database and
        // one of them is chosen as the deadlock victim. Measured on 2026-08-21: a
        // batch run lost 15 SqlServerRunScoreStoreContractTests cases at once
        // because UpgradeMigrationsTableAsync was the victim and the raw
        // SqlException escaped the class fixture's InitializeAsync. All four
        // statements are idempotent — IF NOT EXISTS-guarded DDL plus one SELECT —
        // so retrying is as safe here as it is inside ApplyOneAsync (K-540).
        await RetryOnTransientConflictAsync(
            "The Tracon schema could not be created",
            token => ExecuteAsync(connection, sql.CreateSchema, token),
            cancellationToken).ConfigureAwait(false);

        await RetryOnTransientConflictAsync(
            "The Tracon migration ledger could not be created",
            token => ExecuteAsync(connection, sql.CreateMigrationsTable, token),
            cancellationToken).ConfigureAwait(false);

        // Runs as its OWN command, completed before any InsertMigration text
        // is compiled — see the 🚨 on SqlDialect.UpgradeMigrationsTableAsync.
        await RetryOnTransientConflictAsync(
            "The Tracon migration ledger could not be upgraded",
            token => _context.Dialect.UpgradeMigrationsTableAsync(
                connection,
                _context.CommandTimeoutSeconds,
                token),
            cancellationToken).ConfigureAwait(false);

        var applied = await RetryOnTransientConflictAsync(
            "The Tracon migration ledger could not be read",
            token => ReadAppliedAsync(connection, token),
            cancellationToken).ConfigureAwait(false);

        // 🚨 Before anything is migrated, not after: a database that still
        // holds a non-canonical tenant_id must not have further schema laid on
        // top of it. On a fresh database the catalog is empty and this is a
        // no-op. See TenantIdCaseGuard for why it refuses rather than folds.
        //
        // Under the SAME transient-conflict retry as every bootstrap step
        // above, and for the reason written there: the migration lock is
        // scoped to the SCHEMA, so two schemas bootstrapping at once meet on
        // catalog objects shared by the whole database and one is chosen as
        // the deadlock victim. This step reads sys.columns / information_schema
        // and then scans every tenant table, so it is a LIKELIER victim than
        // the four before it, not a rarer one. It is read-only, therefore
        // retrying is as safe here as it is there (K-540).
        await RetryOnTransientConflictAsync(
            "The Tracon tenant_id case guard could not run",
            token => TenantIdCaseGuard.VerifyAsync(_context, connection, token),
            cancellationToken).ConfigureAwait(false);

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

        if (count > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Tracon applied {Count} migration(s). Schema: {Schema}.",
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
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

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
                "Tracon's migration ledger has a core-numbered entry (id {Id}) whose file was later " +
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

        throw new TraconException(
            $"Migration '{migration.Name}' has been applied to the database but the file's content has changed. " +
            $"Checksum in the database: {record.Checksum}, checksum of the file: {migration.Checksum}. " +
            "An applied migration is never edited; add a new migration file for the change.");
    }

    /// <summary>
    /// The number of retries when a migration races another schema's
    /// concurrent migration over a catalog object shared across the whole
    /// database (for example PostgreSQL's <c>CREATE EXTENSION</c>). The race
    /// surfaces in TWO shapes and both are transient: a uniqueness violation
    /// when both sides insert the same catalog row, and a DEADLOCK when they
    /// take the same locks in opposite orders.
    /// </summary>
    private const int TransientConflictRetryAttempts = 8;

    /// <summary>
    /// Decides whether the database error is a TRANSIENT conflict that the same
    /// statement can survive by being sent again.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> when the statement may be retried.</returns>
    /// <remarks>
    /// The race wears two faces and both are transient: a uniqueness violation
    /// when two sides insert the same catalog row, and a DEADLOCK when they take
    /// the same locks in opposite orders. The server has ALREADY rolled the
    /// victim back, so a retry is correct for it too.
    /// </remarks>
    private bool IsTransientConflict(Exception exception)
        => _context.Dialect.IsUniqueViolation(exception) || _context.Dialect.IsDeadlock(exception);

    /// <summary>Waits before the next attempt, spreading a crowd of racing runners out.</summary>
    /// <param name="attempt">The attempt that just failed, 1-based.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// The delay is RANDOM on purpose: when dozens of schemas migrate for the
    /// first time at once, a fixed delay would make them all retry at the same
    /// instant and would not spread the crowd out (herd avoidance).
    /// </remarks>
    private static Task DelayAfterTransientConflictAsync(int attempt)
        => Task.Delay(
            TimeSpan.FromMilliseconds(Random.Shared.Next(10, 40) * attempt),
            CancellationToken.None);

    /// <summary>
    /// Runs a single statement, retrying it while the database reports a
    /// transient conflict, and turns the last failure into a readable error.
    /// </summary>
    /// <typeparam name="T">The statement's result type.</typeparam>
    /// <param name="what">What was being done, used to build the error message.</param>
    /// <param name="attemptAsync">The statement to run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The statement's result.</returns>
    /// <exception cref="TraconException">
    /// The conflict did not clear within <see cref="TransientConflictRetryAttempts"/>
    /// attempts, or the database reported a different error.
    /// </exception>
    /// <remarks>
    /// The caller must only pass an IDEMPOTENT statement: a retry re-sends it
    /// whole. Every statement that uses this is either "IF NOT EXISTS"-guarded
    /// DDL or a read.
    /// </remarks>
    private async ValueTask<T> RetryOnTransientConflictAsync<T>(
        string what,
        Func<CancellationToken, ValueTask<T>> attemptAsync,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await attemptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbException ex) when (
                IsTransientConflict(ex) && attempt < TransientConflictRetryAttempts)
            {
                await DelayAfterTransientConflictAsync(attempt).ConfigureAwait(false);
            }
            catch (DbException ex) when (_context.Dialect.DescribeDatabaseError(ex) is { } description)
            {
                throw new TraconException($"{what}: {description}.", ex);
            }
        }
    }

    /// <summary>The result-less overload of <see cref="RetryOnTransientConflictAsync{T}"/>.</summary>
    /// <param name="what">What was being done, used to build the error message.</param>
    /// <param name="attemptAsync">The statement to run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    private ValueTask RetryOnTransientConflictAsync(
        string what,
        Func<CancellationToken, ValueTask> attemptAsync,
        CancellationToken cancellationToken)
    {
        return new ValueTask(RunAsync());

        async Task RunAsync()
            => await RetryOnTransientConflictAsync<object?>(
                what,
                async token =>
                {
                    await attemptAsync(token).ConfigureAwait(false);

                    return null;
                },
                cancellationToken).ConfigureAwait(false);
    }

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

        for (var attempt = 1; attempt <= TransientConflictRetryAttempts; attempt++)
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
                    IsTransientConflict(ex) && attempt < TransientConflictRetryAttempts)
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
                    //
                    // 🚨 A DEADLOCK is the same race wearing another face and it
                    // was NOT caught here (measured 2026-08-21): the second catch
                    // below wrapped it in a TraconException and the whole
                    // fixture failed to come up — five SqlServer contract cases
                    // died on "Migration '0017_approval_conditions' could not be
                    // applied: ... (error 1205, state 51)". The server has ALREADY
                    // rolled the victim back, so the same retry is correct for it.
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    await DelayAfterTransientConflictAsync(attempt).ConfigureAwait(false);
                }
                catch (DbException ex) when (_context.Dialect.DescribeDatabaseError(ex) is { } description)
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                    throw new TraconException(
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
