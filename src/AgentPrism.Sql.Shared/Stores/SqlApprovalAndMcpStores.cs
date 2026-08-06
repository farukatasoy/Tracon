using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Kalici onay kurallarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// Kurallar her tool cagrisinda okunur; sorgu <c>(tenant_id, created_at)</c>
/// indeksinden gecer. Kiraci filtresi <strong>her</strong> islemde uygulanir.
/// </remarks>
internal sealed class SqlToolApprovalRuleStore : IToolApprovalRuleStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir kural deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlToolApprovalRuleStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectToolApprovalRules);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadRule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ToolApprovalRule> AddAsync(
        ToolApprovalRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var command = CreateCommand(_sql.InsertToolApprovalRule);
        DbHelpers.Add(command, "id", rule.Id == Guid.Empty ? AgentPrismId.NewId() : rule.Id);
        DbHelpers.Add(command, "tenant_id", rule.TenantId);
        AddNullableText(command, "agent_name", rule.AgentName);
        DbHelpers.Add(command, "tool_name", rule.ToolName);
        AddNullableText(command, "arguments_hash", rule.ArgumentsHash);
        AddNullableText(command, "created_by", rule.CreatedBy);
        Dialect.AddTimestamp(command, "created_at", rule.CreatedAt);

        // ON CONFLICT ... DO UPDATE (degistirmeyen bir atama ile) kullaniliyor:
        // DO NOTHING satiri dondurmez ve mevcut kurali ikinci bir sorguyla
        // okumak gerekirdi.
        var saved = await DbHelpers.ReadSingleAsync(command, ReadRule, cancellationToken).ConfigureAwait(false);

        return saved ?? rule;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.DeleteToolApprovalRule);
        DbHelpers.Add(command, "id", ruleId);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static ToolApprovalRule ReadRule(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = DbHelpers.GetNullableString(reader, 2),
            ToolName = reader.GetString(3),
            ArgumentsHash = DbHelpers.GetNullableString(reader, 4),
            CreatedBy = DbHelpers.GetNullableString(reader, 5),
            CreatedAt = DbHelpers.GetTimestamp(reader, 6),
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);
}

/// <summary>
/// MCP sunucu tanimlarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <strong>Sir tasimaz.</strong> Yalnizca kimlik dogrulama degerinin okunacagi
/// yapilandirma anahtarinin adi saklanir; degerin kendisi hicbir zaman bu
/// tabloya yazilmaz (karar K-059).
/// </remarks>
internal sealed class SqlMcpServerStore : IMcpServerStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir MCP sunucu deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlMcpServerStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectMcpServers);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadServer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<McpServerDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.SelectMcpServer);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(command, ReadServer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var command = CreateCommand(_sql.UpsertMcpServer);
        DbHelpers.Add(command, "id", server.Id == Guid.Empty ? AgentPrismId.NewId() : server.Id);
        DbHelpers.Add(command, "tenant_id", server.TenantId);
        DbHelpers.Add(command, "name", server.Name);
        AddNullableText(command, "description", server.Description);
        DbHelpers.Add(command, "endpoint", server.Endpoint.ToString());
        DbHelpers.Add(command, "transport", (short)server.Transport);
        AddNullableText(command, "authorization_configuration_key", server.AuthorizationConfigurationKey);
        Dialect.AddJsonb(command, "headers", WriteHeaders(server.Headers));
        DbHelpers.Add(command, "enabled", server.Enabled);
        DbHelpers.Add(command, "requires_approval", server.RequiresApproval);
        Dialect.AddTimestamp(command, "now", DateTimeOffset.UtcNow);
        DbHelpers.Add(command, "oauth_enabled", server.OAuthEnabled);
        AddNullableText(command, "oauth_client_id", server.OAuthClientId);
        AddNullableText(command, "oauth_client_secret_configuration_key", server.OAuthClientSecretConfigurationKey);
        AddNullableText(command, "oauth_scopes", server.OAuthScopes);
        DbHelpers.Add(command, "oauth_authorization_mode", (short)server.OAuthAuthorizationMode);

        var saved = await DbHelpers.ReadSingleAsync(command, ReadServer, cancellationToken).ConfigureAwait(false);

        return saved ?? server;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var command = CreateCommand(_sql.DeleteMcpServer);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    // Basliklar duz bir metin sozlugudur; Utf8JsonWriter ile elle yazilir
    // (yansimasiz, AOT uyumlu). Polimorfik ayrac tasimadigi icin jsonb guvenlidir.
    private static string WriteHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var buffer = new MemoryStream();
        var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();

        foreach (var pair in headers)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static Dictionary<string, string> ReadHeaders(string? json)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(json))
        {
            return headers;
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return headers;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                headers[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return headers;
    }

    private static McpServerDefinition ReadServer(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = DbHelpers.GetNullableString(reader, 3),
            Endpoint = new Uri(reader.GetString(4)),
            Transport = (McpTransportMode)reader.GetInt16(5),
            AuthorizationConfigurationKey = DbHelpers.GetNullableString(reader, 6),
            Headers = ReadHeaders(DbHelpers.GetNullableString(reader, 7)),
            Enabled = reader.GetBoolean(8),
            RequiresApproval = reader.GetBoolean(9),
            CreatedAt = DbHelpers.GetTimestamp(reader, 10),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 11),
            OAuthEnabled = reader.GetBoolean(12),
            OAuthClientId = DbHelpers.GetNullableString(reader, 13),
            OAuthClientSecretConfigurationKey = DbHelpers.GetNullableString(reader, 14),
            OAuthScopes = DbHelpers.GetNullableString(reader, 15),
            OAuthAuthorizationMode = (McpOAuthAuthorizationMode)reader.GetInt16(16),
        };

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);
}

/// <summary>
/// Kiraci kayitlarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// Kiraci kaydi zorunlu degildir: diger tablolardaki <c>tenant_id</c> bu tablonun
/// <c>slug</c> degeriyle ayni metindir ancak yabanci anahtarla baglanmaz. Kaydi
/// silmek kiracinin verisini silmez.
/// </remarks>
internal sealed class SqlTenantStore : ITenantStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir kiraci deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlTenantStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    [TenantAgnostic(
        "Kiraci defterinin kendisi kiracilarin USTUNDEDIR: listeleme, kurulumdaki butun kiracilari dondurur ve yonetim yuzeyi icindir (Admin policy). Bir kiraci filtresi burada 'kendini listele'ye indirgenir ve anlamsizdir.")]
    public async ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectTenants);

        return await DbHelpers.ReadListAsync(command, ReadTenant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Kiraci defterinin kendisi kiracilarin USTUNDEDIR; kayit yeni bir kiraci ACAR, var olan bir kiracinin verisine dokunmaz.")]
    public async ValueTask<TenantDescriptor> SaveAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var command = CreateCommand(_sql.UpsertTenantDescriptor);
        DbHelpers.Add(command, "id", tenant.Id == Guid.Empty ? AgentPrismId.NewId() : tenant.Id);
        DbHelpers.Add(command, "slug", tenant.Slug);
        DbHelpers.Add(command, "display_name", tenant.DisplayName);
        Dialect.AddTimestamp(command, "created_at", tenant.CreatedAt == default ? DateTimeOffset.UtcNow : tenant.CreatedAt);

        var saved = await DbHelpers.ReadSingleAsync(command, ReadTenant, cancellationToken).ConfigureAwait(false);

        return saved ?? tenant;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Kiraci defterinin kendisi kiracilarin USTUNDEDIR; silme bir kiraci KAYDINI kaldirir ve yonetim yuzeyi icindir.")]
    public async ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var command = CreateCommand(_sql.DeleteTenant);
        DbHelpers.Add(command, "slug", slug);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static TenantDescriptor ReadTenant(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            Slug = reader.GetString(1),
            DisplayName = reader.GetString(2),
            CreatedAt = DbHelpers.GetTimestamp(reader, 3),
        };
}
