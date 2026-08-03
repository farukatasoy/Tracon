using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// Kalici onay kurallarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// Kurallar her tool cagrisinda okunur; sorgu <c>(tenant_id, created_at)</c>
/// indeksinden gecer. Kiraci filtresi <strong>her</strong> islemde uygulanir.
/// </remarks>
public sealed class PostgresToolApprovalRuleStore : IToolApprovalRuleStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir kural deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresToolApprovalRuleStore(
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
    public async ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectToolApprovalRules);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadRule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ToolApprovalRule> AddAsync(
        ToolApprovalRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var command = CreateCommand(_sql.InsertToolApprovalRule);
        command.Parameters.AddWithValue("id", rule.Id == Guid.Empty ? AgentPrismId.NewId() : rule.Id);
        command.Parameters.AddWithValue("tenant_id", rule.TenantId);
        AddNullableText(command, "agent_name", rule.AgentName);
        command.Parameters.AddWithValue("tool_name", rule.ToolName);
        AddNullableText(command, "arguments_hash", rule.ArgumentsHash);
        AddNullableText(command, "created_by", rule.CreatedBy);
        command.Parameters.AddWithValue("created_at", rule.CreatedAt.UtcDateTime);

        // ON CONFLICT ... DO UPDATE (degistirmeyen bir atama ile) kullaniliyor:
        // DO NOTHING satiri dondurmez ve mevcut kurali ikinci bir sorguyla
        // okumak gerekirdi.
        var saved = await NpgsqlHelpers.ReadSingleAsync(command, ReadRule, cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddWithValue("id", ruleId);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static ToolApprovalRule ReadRule(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = NpgsqlHelpers.GetNullableString(reader, 2),
            ToolName = reader.GetString(3),
            ArgumentsHash = NpgsqlHelpers.GetNullableString(reader, 4),
            CreatedBy = NpgsqlHelpers.GetNullableString(reader, 5),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 6),
        };

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });
}

/// <summary>
/// MCP sunucu tanimlarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <strong>Sir tasimaz.</strong> Yalnizca kimlik dogrulama degerinin okunacagi
/// yapilandirma anahtarinin adi saklanir; degerin kendisi hicbir zaman bu
/// tabloya yazilmaz (karar K-059).
/// </remarks>
public sealed class PostgresMcpServerStore : IMcpServerStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir MCP sunucu deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresMcpServerStore(
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
    public async ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectMcpServers);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadServer, cancellationToken).ConfigureAwait(false);
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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadServer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var command = CreateCommand(_sql.UpsertMcpServer);
        command.Parameters.AddWithValue("id", server.Id == Guid.Empty ? AgentPrismId.NewId() : server.Id);
        command.Parameters.AddWithValue("tenant_id", server.TenantId);
        command.Parameters.AddWithValue("name", server.Name);
        AddNullableText(command, "description", server.Description);
        command.Parameters.AddWithValue("endpoint", server.Endpoint.ToString());
        command.Parameters.AddWithValue("transport", (short)server.Transport);
        AddNullableText(command, "authorization_configuration_key", server.AuthorizationConfigurationKey);
        command.Parameters.Add(new NpgsqlParameter("headers", NpgsqlDbType.Jsonb)
        {
            Value = WriteHeaders(server.Headers),
        });
        command.Parameters.AddWithValue("enabled", server.Enabled);
        command.Parameters.AddWithValue("requires_approval", server.RequiresApproval);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("oauth_enabled", server.OAuthEnabled);
        AddNullableText(command, "oauth_client_id", server.OAuthClientId);
        AddNullableText(command, "oauth_client_secret_configuration_key", server.OAuthClientSecretConfigurationKey);
        AddNullableText(command, "oauth_scopes", server.OAuthScopes);
        command.Parameters.AddWithValue("oauth_authorization_mode", (short)server.OAuthAuthorizationMode);

        var saved = await NpgsqlHelpers.ReadSingleAsync(command, ReadServer, cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

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

    private static McpServerDefinition ReadServer(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = NpgsqlHelpers.GetNullableString(reader, 3),
            Endpoint = new Uri(reader.GetString(4)),
            Transport = (McpTransportMode)reader.GetInt16(5),
            AuthorizationConfigurationKey = NpgsqlHelpers.GetNullableString(reader, 6),
            Headers = ReadHeaders(NpgsqlHelpers.GetNullableString(reader, 7)),
            Enabled = reader.GetBoolean(8),
            RequiresApproval = reader.GetBoolean(9),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 10),
            UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 11),
            OAuthEnabled = reader.GetBoolean(12),
            OAuthClientId = NpgsqlHelpers.GetNullableString(reader, 13),
            OAuthClientSecretConfigurationKey = NpgsqlHelpers.GetNullableString(reader, 14),
            OAuthScopes = NpgsqlHelpers.GetNullableString(reader, 15),
            OAuthAuthorizationMode = (McpOAuthAuthorizationMode)reader.GetInt16(16),
        };

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });
}

/// <summary>
/// Kiraci kayitlarini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// Kiraci kaydi zorunlu degildir: diger tablolardaki <c>tenant_id</c> bu tablonun
/// <c>slug</c> degeriyle ayni metindir ancak yabanci anahtarla baglanmaz. Kaydi
/// silmek kiracinin verisini silmez.
/// </remarks>
public sealed class PostgresTenantStore : ITenantStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir kiraci deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresTenantStore(
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
    public async ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectTenants);

        return await NpgsqlHelpers.ReadListAsync(command, ReadTenant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TenantDescriptor> SaveAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var command = CreateCommand(_sql.UpsertTenantDescriptor);
        command.Parameters.AddWithValue("id", tenant.Id == Guid.Empty ? AgentPrismId.NewId() : tenant.Id);
        command.Parameters.AddWithValue("slug", tenant.Slug);
        command.Parameters.AddWithValue("display_name", tenant.DisplayName);
        command.Parameters.AddWithValue(
            "created_at",
            tenant.CreatedAt == default ? DateTimeOffset.UtcNow.UtcDateTime : tenant.CreatedAt.UtcDateTime);

        var saved = await NpgsqlHelpers.ReadSingleAsync(command, ReadTenant, cancellationToken).ConfigureAwait(false);

        return saved ?? tenant;
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var command = CreateCommand(_sql.DeleteTenant);
        command.Parameters.AddWithValue("slug", slug);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static TenantDescriptor ReadTenant(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            Slug = reader.GetString(1),
            DisplayName = reader.GetString(2),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 3),
        };
}
