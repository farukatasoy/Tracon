using System.Data.Common;

namespace AgentPrism;

/// <summary>Saklama politikalarini ve kosu gecmisini kalici olarak saklayan depo.</summary>
/// <remarks>
/// Davranis sozlesmesi <see cref="InMemoryRetentionPolicyStore"/> ile birebir
/// aynidir. Veri duzlemi (fiili silme) icin bkz. <see cref="SqlRetentionStore"/>.
/// </remarks>
internal sealed class SqlRetentionPolicyStore : IRetentionPolicyStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir saklama politikasi deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlRetentionPolicyStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RetentionPolicy>> ListPoliciesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectRetentionPolicies);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadPolicy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RetentionPolicy?> GetPolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var command = CreateCommand(_sql.SelectRetentionPolicy);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "target", target);

        var found = await DbHelpers.ReadSingleAsync(command, ReadPolicy, cancellationToken).ConfigureAwait(false);

        if (found is not null || string.Equals(tenantId, "*", StringComparison.Ordinal))
        {
            return found;
        }

        // Kiraciya ozel kayit yoksa "*" genel varsayilanina duser.
        var fallback = CreateCommand(_sql.SelectRetentionPolicy);
        DbHelpers.Add(fallback, "tenant_id", "*");
        DbHelpers.Add(fallback, "target", target);

        return await DbHelpers.ReadSingleAsync(fallback, ReadPolicy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RetentionPolicy> SavePolicyAsync(
        RetentionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var command = CreateCommand(_sql.UpsertRetentionPolicy);
        DbHelpers.Add(command, "id", policy.Id);
        DbHelpers.Add(command, "tenant_id", policy.TenantId);
        DbHelpers.Add(command, "target", policy.Target);
        Dialect.AddInt32(command, "max_age_days", policy.MaxAgeDays);
        Dialect.AddInt64(command, "max_rows", policy.MaxRows);
        DbHelpers.Add(command, "archive", policy.Archive);
        DbHelpers.Add(command, "enabled", policy.Enabled);
        Dialect.AddTimestamp(command, "created_at", policy.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", policy.UpdatedAt);

        var saved = await DbHelpers.ReadSingleAsync(command, ReadPolicy, cancellationToken).ConfigureAwait(false);

        return saved ?? policy;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeletePolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var command = CreateCommand(_sql.DeleteRetentionPolicy);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "target", target);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<RetentionRun> CreateRunAsync(RetentionRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var command = CreateCommand(_sql.InsertRetentionRun);
        DbHelpers.Add(command, "id", run.Id);
        DbHelpers.Add(command, "tenant_id", run.TenantId);
        DbHelpers.Add(command, "target", run.Target);
        Dialect.AddTimestamp(command, "started_at", run.StartedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return run;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Ilerleme, kosuyu ACAN yurutucu tarafindan kendi urettigi kosu kimligiyle yazilir; cagride ayri bir kiraci niyeti yoktur.")]
    public async ValueTask AppendRunProgressAsync(
        Guid runId,
        long deletedDelta,
        long archivedDelta,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.UpdateRetentionRunProgress);
        DbHelpers.Add(command, "id", runId);
        DbHelpers.Add(command, "deleted_delta", deletedDelta);
        DbHelpers.Add(command, "archived_delta", archivedDelta);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "AppendRunProgressAsync ile ayni gerekce: kosu kimligi CreateRunAsync'ten gelir.")]
    public async ValueTask CompleteRunAsync(
        Guid runId,
        DateTimeOffset completedAt,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.CompleteRetentionRun);
        DbHelpers.Add(command, "id", runId);
        Dialect.AddTimestamp(command, "completed_at", completedAt);
        Dialect.AddText(command, "error", errorMessage);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RetentionRun>> ListRunsAsync(
        string tenantId,
        string? target,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectRetentionRuns);
        DbHelpers.Add(command, "tenant_id", tenantId);
        Dialect.AddText(command, "target", target);
        DbHelpers.Add(command, "skip", Math.Max(0, skip));
        DbHelpers.Add(command, "take", Math.Max(1, take));

        return await DbHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static RetentionPolicy ReadPolicy(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Target = reader.GetString(2),
            MaxAgeDays = DbHelpers.GetNullableInt32(reader, 3),
            MaxRows = DbHelpers.GetNullableInt64(reader, 4),
            Archive = reader.GetBoolean(5),
            Enabled = reader.GetBoolean(6),
            CreatedAt = DbHelpers.GetTimestamp(reader, 7),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 8),
        };

    private static RetentionRun ReadRun(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Target = reader.GetString(2),
            DeletedRows = reader.GetInt64(3),
            ArchivedRows = reader.GetInt64(4),
            StartedAt = DbHelpers.GetTimestamp(reader, 5),
            CompletedAt = DbHelpers.GetNullableTimestamp(reader, 6),
            Error = DbHelpers.GetNullableString(reader, 7),
        };
}
