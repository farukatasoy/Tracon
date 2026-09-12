using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Stores persistent approval rules in the SQL database.
/// </summary>
/// <remarks>
/// Rules are read on every tool call; the query goes through the
/// <c>(tenant_id, created_at)</c> index. The tenant filter applies on
/// <strong>every</strong> operation.
/// </remarks>
internal sealed class SqlToolApprovalRuleStore : IToolApprovalRuleStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new rule store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlToolApprovalRuleStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
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
        DbHelpers.Add(command, "id", rule.Id == Guid.Empty ? TraconId.NewId() : rule.Id);
        DbHelpers.Add(command, "tenant_id", rule.TenantId);
        AddNullableText(command, "agent_name", rule.AgentName);
        DbHelpers.Add(command, "tool_name", rule.ToolName);
        AddNullableText(command, "arguments_hash", rule.ArgumentsHash);
        Dialect.AddJsonb(
            command,
            "argument_conditions",
            rule.ArgumentConditions.Count == 0
                ? null
                : JsonSerializer.Serialize(rule.ArgumentConditions, TraconJsonContext.Default.IReadOnlyListToolArgumentCondition));
        AddNullableText(command, "conditions_hash", ComputeConditionsHash(rule.ArgumentConditions));
        AddNullableText(command, "created_by", rule.CreatedBy);
        Dialect.AddTimestamp(command, "created_at", rule.CreatedAt);

        // Uses ON CONFLICT ... DO UPDATE (with a no-op assignment): DO NOTHING
        // does not return a row, which would require a second query to read
        // the existing rule.
        var saved = await DbHelpers.ReadSingleAsync(command, ReadRule, cancellationToken).ConfigureAwait(false);

        return saved ?? rule;
    }

    /// <summary>
    /// Computes the deterministic fingerprint of a condition set that backs the
    /// <c>conditions_hash</c> column and its uniqueness key.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> for an empty list — symmetric with
    /// <see cref="ToolApprovalRule.ArgumentsHash"/>'s "no constraint" meaning, and
    /// keeps the <c>COALESCE(conditions_hash, '')</c> uniqueness index working the
    /// same way for both columns. Conditions are sorted before hashing: the same
    /// SET of conditions must produce the same hash regardless of the order they
    /// were written in.
    /// </remarks>
    private static string? ComputeConditionsHash(IReadOnlyList<ToolArgumentCondition> conditions)
    {
        if (conditions.Count == 0)
        {
            return null;
        }

        var ordered = conditions
            .Select(static condition => (condition.Path, condition.Operator, Text: CanonicalizeJson(condition.Value)))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(static entry => (int)entry.Operator)
            .ThenBy(static entry => entry.Text, StringComparer.Ordinal);

        var builder = new StringBuilder();

        foreach (var entry in ordered)
        {
            // Same separator convention as ToolApprovalRuleEvaluator.ComputeArgumentsHash:
            // the unit/record separator cannot appear in the fields being joined, so
            // two different condition sets cannot collide.
            builder.Append(entry.Path)
                .Append('\u001F')
                .Append((int)entry.Operator)
                .Append('\u001F')
                .Append(entry.Text)
                .Append('\u001E');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash);
    }

    /// <summary>Renders a JSON value with no incidental whitespace, so equal values always hash the same.</summary>
    private static string CanonicalizeJson(JsonElement value)
        => value.ValueKind == JsonValueKind.Array
            ? "[" + string.Join(",", value.EnumerateArray().Select(CanonicalizeJson)) + "]"
            : value.GetRawText();

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

    // 🚨 New columns are ALWAYS appended at the end (docs/hafiza/postgresql.md):
    // argument_conditions reads by fixed ordinal 7, after the original seven columns.
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
            ArgumentConditions = ReadConditions(DbHelpers.GetNullableString(reader, 7)),
        };

    private static IReadOnlyList<ToolArgumentCondition> ReadConditions(string? json)
        => string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize(json, TraconJsonContext.Default.IReadOnlyListToolArgumentCondition) ?? [];

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);
}

/// <summary>
/// Stores MCP server definitions in the SQL database.
/// </summary>
/// <remarks>
/// <strong>Carries no secret.</strong> Only the name of the configuration key
/// whose value resolves the authorization credential is stored; the value
/// itself is never written to this table.
/// </remarks>
internal sealed class SqlMcpServerStore : IMcpServerStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new MCP server store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlMcpServerStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
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
        DbHelpers.Add(command, "id", server.Id == Guid.Empty ? TraconId.NewId() : server.Id);
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

    // Headers are a flat string dictionary; written by hand with Utf8JsonWriter
    // (reflection-free, AOT-compatible). jsonb is safe here because the value
    // carries no polymorphic type discriminator.
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
/// Stores tenant records in the SQL database.
/// </summary>
/// <remarks>
/// A tenant record is not mandatory: <c>tenant_id</c> in other tables is the
/// same text as this table's <c>slug</c> value, but it is not linked by a
/// foreign key. Deleting the record does not delete the tenant's data.
/// </remarks>
internal sealed class SqlTenantStore : ITenantStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new tenant store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlTenantStore(
        SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    [TenantAgnostic(
        "The tenant registry itself sits ABOVE tenants: listing returns every tenant in the installation and exists for the management surface (Admin policy). A tenant filter here reduces to 'list yourself' and is meaningless.")]
    public async ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectTenants);

        return await DbHelpers.ReadListAsync(command, ReadTenant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The tenant registry itself sits ABOVE tenants; saving OPENS a new tenant and does not touch an existing tenant's data.")]
    public async ValueTask<TenantDescriptor> SaveAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var command = CreateCommand(_sql.UpsertTenantDescriptor);
        DbHelpers.Add(command, "id", tenant.Id == Guid.Empty ? TraconId.NewId() : tenant.Id);
        DbHelpers.Add(command, "slug", tenant.Slug);
        DbHelpers.Add(command, "display_name", tenant.DisplayName);
        Dialect.AddTimestamp(command, "created_at", tenant.CreatedAt == default ? DateTimeOffset.UtcNow : tenant.CreatedAt);

        var saved = await DbHelpers.ReadSingleAsync(command, ReadTenant, cancellationToken).ConfigureAwait(false);

        return saved ?? tenant;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The tenant registry itself sits ABOVE tenants; deleting removes a tenant's RECORD and exists for the management surface.")]
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
