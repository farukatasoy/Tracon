using System.Data.Common;
using System.Text.Json;

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
internal sealed class SqlSessionStore : ISessionStore
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

    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir oturum deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlSessionStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

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
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId ?? _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        // `json` sutunu metni AYNEN saklar. `jsonb` anahtarlari yeniden siralar
        // ve System.Text.Json'un `$type` ayracini gecersiz kilar (karar K-027).
        Dialect.AddJson(command, "state", record.State.GetRawText());
        DbHelpers.Add(command, "schema_version", CurrentSchemaVersion);
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Duz bir <c>INSERT</c>'tir; ayni (tenant_id, id) ile eszamanli ikinci bir
    /// cagri benzersizlik ihlaline duser ve <see cref="SqlDialect.IsUniqueViolation"/>
    /// ile yakalanip <see langword="false"/>'a cevrilir — tipki <c>SqlIdempotencyStore.ReserveAsync</c>'in
    /// yaptigi gibi. Bu, <see cref="SaveAsync"/>'in kosulsuz uzerine yazmasinin
    /// aksine, ayni YENI oturuma gelen eszamanli iki ilk istekten yalniz birinin
    /// oturumu "kazanmasini" saglar (HATA-004).
    /// </remarks>
    public async ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var command = CreateCommand(_sql.InsertSession);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId ?? _tenantContext.TenantId);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        Dialect.AddJson(command, "state", record.State.GetRawText());
        DbHelpers.Add(command, "schema_version", CurrentSchemaVersion);
        Dialect.AddTimestamp(command, "created_at", record.CreatedAt);
        Dialect.AddTimestamp(command, "updated_at", record.UpdatedAt);

        try
        {
            await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.SelectSession);
        DbHelpers.Add(command, "id", sessionId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ReadSingleAsync(
            command,
            reader => new SessionRecord
            {
                Id = sessionId,
                AgentName = reader.GetString(0),
                State = ReadState(sessionId, reader.GetString(1), reader.GetInt32(2)),
                CreatedAt = DbHelpers.GetTimestamp(reader, 3),
                UpdatedAt = DbHelpers.GetTimestamp(reader, 4),
                TenantId = reader.GetString(5),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.DeleteSession);
        DbHelpers.Add(command, "id", sessionId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectSessions);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddText(command, "agent_name", query.AgentName);
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Max(query.Take, 0));

        return await DbHelpers.ReadListAsync(
            command,
            static reader =>
            {
                var id = reader.GetString(0);

                return new SessionRecord
                {
                    Id = id,
                    AgentName = reader.GetString(1),
                    State = ReadState(id, reader.GetString(2), reader.GetInt32(3)),
                    CreatedAt = DbHelpers.GetTimestamp(reader, 4),
                    UpdatedAt = DbHelpers.GetTimestamp(reader, 5),
                    TenantId = reader.GetString(6),
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

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
