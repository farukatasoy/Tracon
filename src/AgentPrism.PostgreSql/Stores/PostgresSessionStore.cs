using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// Serilestirilmis agent oturumlarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <para>
/// Oturum durumu Microsoft Agent Framework'un <c>SerializeSessionAsync</c> ciktisidir
/// ve <strong>opak</strong> kabul edilir; icerigi yorumlanmaz.
/// </para>
/// <para>
/// <see cref="CurrentSchemaVersion"/> her satirda saklanir. Ileride serilestirme
/// bicimi degisirse, eski bir bicimi okuyan yeni bir surum sessizce yanlis davranmak
/// yerine anlasilir bir hata verir.
/// </para>
/// </remarks>
public sealed class PostgresSessionStore : ISessionStore
{
    /// <summary>
    /// Yazilan oturum durumunun bicim surumu.
    /// </summary>
    /// <remarks>
    /// Bu deger yalnizca AgentPrism'in oturum satirini nasil yorumladigini anlatir;
    /// durumun kendi ic yapisini Microsoft Agent Framework belirler. Bicim degisirse
    /// deger artirilir ve gecis yolu yazilir.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly ITenantContext _tenantContext;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir oturum deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresSessionStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _tenantContext = tenantContext;
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ayni kimlikle kayit varsa <see cref="SessionRecord.CreatedAt"/> korunur;
    /// "olusturulma zamani" ilk yazmaya aittir. <see cref="InMemorySessionStore"/>
    /// ayni davranisi gosterir.
    /// </remarks>
    public async ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = CreateCommand(_sql.UpsertSession);
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("tenant_id", record.TenantId ?? _tenantContext.TenantId);
        command.Parameters.AddWithValue("agent_name", record.AgentName);
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Json)
        {
            // `json` sutunu metni AYNEN saklar. `jsonb` anahtarlari yeniden siralar
            // ve System.Text.Json'un `$type` ayracini gecersiz kilar (karar K-027).
            Value = record.State.GetRawText(),
        });
        command.Parameters.AddWithValue("schema_version", CurrentSchemaVersion);
        command.Parameters.AddWithValue("created_at", record.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", record.UpdatedAt.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.SelectSession);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        return await NpgsqlHelpers.ReadSingleAsync(
            command,
            reader => new SessionRecord
            {
                Id = sessionId,
                AgentName = reader.GetString(0),
                State = ReadState(sessionId, reader.GetString(1), reader.GetInt32(2)),
                CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 3),
                UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 4),
                TenantId = reader.GetString(5),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.DeleteSession);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectSessions);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? _tenantContext.TenantId);
        command.Parameters.Add(new NpgsqlParameter("agent_name", NpgsqlDbType.Text)
        {
            Value = (object?)query.AgentName ?? DBNull.Value,
        });
        command.Parameters.AddWithValue("skip", Math.Max(query.Skip, 0));
        command.Parameters.AddWithValue("take", Math.Max(query.Take, 0));

        return await NpgsqlHelpers.ReadListAsync(
            command,
            static reader =>
            {
                var id = reader.GetString(0);

                return new SessionRecord
                {
                    Id = id,
                    AgentName = reader.GetString(1),
                    State = ReadState(id, reader.GetString(2), reader.GetInt32(3)),
                    CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 4),
                    UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 5),
                    TenantId = reader.GetString(6),
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static JsonElement ReadState(string sessionId, string json, int schemaVersion)
    {
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new AgentPrismException(
                $"'{sessionId}' oturumu {schemaVersion} numarali bicim surumuyle yazilmis; bu AgentPrism surumu " +
                $"en fazla {CurrentSchemaVersion} surumunu okuyabilir. AgentPrism paketlerini guncelleyin.");
        }

        // JsonDocument sahipligi burada biter; Clone bagimsiz bir kopya dondurur.
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
