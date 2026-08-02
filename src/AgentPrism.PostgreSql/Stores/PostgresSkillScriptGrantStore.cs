using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism;

/// <summary>Script calistirma izinlerini PostgreSQL'de saklayan tenant-yalitimli depo.</summary>
/// <remarks>
/// Izin kaydi <strong>silinmez</strong>, iptal edilir (<c>revoked_at</c>). "Bu
/// sunucuda kim, ne zaman, hangi script'e calistirma yetkisi verdi ve ne zaman
/// geri aldi" sorusu denetim izinden bagimsiz olarak da cevaplanabilmelidir.
/// </remarks>
public sealed class PostgresSkillScriptGrantStore : ISkillScriptGrantStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir izin deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresSkillScriptGrantStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectSkillScriptGrants);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return await NpgsqlHelpers.ReadListAsync(command, ReadGrant, cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("skill_name", skillName);
        command.Parameters.AddWithValue("script_name", scriptName);
        command.Parameters.AddWithValue("instant", instant.UtcDateTime);
        return await NpgsqlHelpers.ReadSingleAsync(command, ReadGrant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<SkillScriptGrant> GrantAsync(
        SkillScriptGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var now = grant.GrantedAt == default ? DateTimeOffset.UtcNow : grant.GrantedAt;
        var command = CreateCommand(_sql.UpsertSkillScriptGrant);
        command.Parameters.AddWithValue("id", grant.Id == Guid.Empty ? AgentPrismId.NewId(now) : grant.Id);
        command.Parameters.AddWithValue("tenant_id", grant.TenantId);
        command.Parameters.AddWithValue("skill_name", grant.SkillName);
        command.Parameters.AddWithValue("script_name", (object?)grant.ScriptName ?? DBNull.Value);
        command.Parameters.AddWithValue("granted_by", (object?)grant.GrantedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("granted_at", now.UtcDateTime);
        command.Parameters.AddWithValue(
            "expires_at",
            grant.ExpiresAt is { } expires ? expires.UtcDateTime : (object)DBNull.Value);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadGrant, cancellationToken).ConfigureAwait(false)
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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("skill_name", skillName);
        command.Parameters.AddWithValue("script_name", (object?)scriptName ?? DBNull.Value);
        command.Parameters.AddWithValue("revoked_at", DateTimeOffset.UtcNow.UtcDateTime);
        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private static SkillScriptGrant ReadGrant(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = reader.GetString(1),
        SkillName = reader.GetString(2),
        ScriptName = NpgsqlHelpers.GetNullableString(reader, 3),
        GrantedBy = NpgsqlHelpers.GetNullableString(reader, 4),
        GrantedAt = NpgsqlHelpers.GetTimestamp(reader, 5),
        ExpiresAt = NpgsqlHelpers.GetNullableTimestamp(reader, 6),
        RevokedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 7),
    };

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;
        return command;
    }
}
