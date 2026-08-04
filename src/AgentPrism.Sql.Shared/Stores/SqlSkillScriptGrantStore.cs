using System.Data.Common;

namespace AgentPrism;

/// <summary>Script calistirma izinlerini PostgreSQL'de saklayan tenant-yalitimli depo.</summary>
/// <remarks>
/// Izin kaydi <strong>silinmez</strong>, iptal edilir (<c>revoked_at</c>). "Bu
/// sunucuda kim, ne zaman, hangi script'e calistirma yetkisi verdi ve ne zaman
/// geri aldi" sorusu denetim izinden bagimsiz olarak da cevaplanabilmelidir.
/// </remarks>
internal sealed class SqlSkillScriptGrantStore : ISkillScriptGrantStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir izin deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlSkillScriptGrantStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectSkillScriptGrants);
        DbHelpers.Add(command, "tenant_id", tenantId);
        return await DbHelpers.ReadListAsync(command, ReadGrant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<SkillScriptGrant?> FindActiveAsync(
        string tenantId,
        string skillName,
        string scriptName,
        DateTimeOffset instant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(skillName);
        ArgumentNullException.ThrowIfNull(scriptName);

        var command = CreateCommand(_sql.SelectActiveSkillScriptGrant);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "skill_name", skillName);
        Dialect.AddText(command, "script_name", scriptName);
        Dialect.AddTimestamp(command, "instant", instant);
        return await DbHelpers.ReadSingleAsync(command, ReadGrant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<SkillScriptGrant> GrantAsync(
        SkillScriptGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var now = grant.GrantedAt == default ? DateTimeOffset.UtcNow : grant.GrantedAt;
        var command = CreateCommand(_sql.UpsertSkillScriptGrant);
        DbHelpers.Add(command, "id", grant.Id == Guid.Empty ? AgentPrismId.NewId(now) : grant.Id);
        DbHelpers.Add(command, "tenant_id", grant.TenantId);
        DbHelpers.Add(command, "skill_name", grant.SkillName);
        Dialect.AddText(command, "script_name", grant.ScriptName);
        Dialect.AddText(command, "granted_by", grant.GrantedBy);
        Dialect.AddTimestamp(command, "granted_at", now);
        Dialect.AddTimestamp(command, "expires_at", grant.ExpiresAt);

        return await DbHelpers.ReadSingleAsync(command, ReadGrant, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException(
                $"'{grant.SkillName}' skill'i icin script calistirma izni kaydedilemedi.");
    }

    /// <inheritdoc />
    public async ValueTask<bool> RevokeAsync(
        string tenantId,
        string skillName,
        string? scriptName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(skillName);

        var command = CreateCommand(_sql.RevokeSkillScriptGrant);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "skill_name", skillName);
        Dialect.AddText(command, "script_name", scriptName);
        Dialect.AddTimestamp(command, "revoked_at", DateTimeOffset.UtcNow);
        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private static SkillScriptGrant ReadGrant(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = reader.GetString(1),
        SkillName = reader.GetString(2),
        ScriptName = DbHelpers.GetNullableString(reader, 3),
        GrantedBy = DbHelpers.GetNullableString(reader, 4),
        GrantedAt = DbHelpers.GetTimestamp(reader, 5),
        ExpiresAt = DbHelpers.GetNullableTimestamp(reader, 6),
        RevokedAt = DbHelpers.GetNullableTimestamp(reader, 7),
    };

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);
}
