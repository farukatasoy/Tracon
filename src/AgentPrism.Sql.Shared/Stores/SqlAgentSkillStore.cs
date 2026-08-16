using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>Tenant-isolated store for skill definitions in the SQL database.</summary>
internal sealed class SqlAgentSkillStore : IAgentSkillStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new skill store.</summary>
    /// <param name="context">The store context.</param>
    public SqlAgentSkillStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectAgentSkills);
        DbHelpers.Add(command, "tenant_id", tenantId);
        var rows = await DbHelpers.ReadListAsync(command, ReadSkill, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < rows.Count; index++)
        {
            var skill = rows[index];
            rows[index] = skill with
            {
                Resources = await ReadResourcesAsync(skill.Id, cancellationToken).ConfigureAwait(false),
                Scripts = await ReadScriptsAsync(skill.Id, cancellationToken).ConfigureAwait(false),
            };
        }

        return rows;
    }

    /// <inheritdoc />
    public async ValueTask<AgentSkillDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.SelectAgentSkill);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        var skill = await DbHelpers.ReadSingleAsync(command, ReadSkill, cancellationToken).ConfigureAwait(false);

        if (skill is { })
        {
            return skill with
            {
                Resources = await ReadResourcesAsync(skill.Id, cancellationToken).ConfigureAwait(false),
                Scripts = await ReadScriptsAsync(skill.Id, cancellationToken).ConfigureAwait(false),
            };
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<AgentSkillDefinition> SaveAsync(
        AgentSkillDefinition skill,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var now = DateTimeOffset.UtcNow;
        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var upsert = _context.CreateCommand(_sql.UpsertAgentSkill, connection, transaction);
                ConfigureSkill(upsert, skill, now);
                var saved = await DbHelpers.ReadSingleAsync(upsert, ReadSkill, cancellationToken).ConfigureAwait(false)
                    ?? throw new AgentPrismException($"'{skill.Name}' skill'i kaydedilemedi.");

                var deleteResources = _context.CreateCommand(_sql.DeleteAgentSkillResources, connection, transaction);
                DbHelpers.Add(deleteResources, "skill_id", saved.Id);
                await DbHelpers.ExecuteAsync(deleteResources, cancellationToken).ConfigureAwait(false);

                foreach (var resource in skill.Resources)
                {
                    var insertResource = _context.CreateCommand(_sql.InsertAgentSkillResource, connection, transaction);
                    DbHelpers.Add(insertResource, "id", AgentPrismId.NewId());
                    DbHelpers.Add(insertResource, "skill_id", saved.Id);
                    DbHelpers.Add(insertResource, "name", resource.Name);
                    Dialect.AddText(insertResource, "description", resource.Description);
                    DbHelpers.Add(insertResource, "media_type", resource.MediaType);
                    DbHelpers.Add(insertResource, "content", resource.Content);
                    Dialect.AddTimestamp(insertResource, "created_at", now);
                    await DbHelpers.ExecuteAsync(insertResource, cancellationToken).ConfigureAwait(false);
                }

                var deleteScripts = _context.CreateCommand(_sql.DeleteAgentSkillScripts, connection, transaction);
                DbHelpers.Add(deleteScripts, "skill_id", saved.Id);
                await DbHelpers.ExecuteAsync(deleteScripts, cancellationToken).ConfigureAwait(false);

                foreach (var script in skill.Scripts)
                {
                    var insertScript = _context.CreateCommand(_sql.InsertAgentSkillScript, connection, transaction);
                    DbHelpers.Add(insertScript, "id", AgentPrismId.NewId());
                    DbHelpers.Add(insertScript, "skill_id", saved.Id);
                    DbHelpers.Add(insertScript, "name", script.Name);
                    Dialect.AddText(insertScript, "description", script.Description);

                    // The extension is stored without a dot and lower-cased: the
                    // interpreter allow-list is searched in this form, and mixing
                    // the two forms would silently make the script unrunnable.
                    DbHelpers.Add(insertScript, "extension", script.Extension.TrimStart('.').ToLowerInvariant());
                    DbHelpers.Add(insertScript, "content", script.Content);
                    Dialect.AddJsonb(insertScript, "parameters_schema", script.ParametersSchema);
                    Dialect.AddTimestamp(insertScript, "created_at", now);
                    await DbHelpers.ExecuteAsync(insertScript, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return saved with { Resources = skill.Resources, Scripts = skill.Scripts };
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.DeleteAgentSkill);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);
        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    private async ValueTask<IReadOnlyList<AgentSkillResourceDefinition>> ReadResourcesAsync(
        Guid skillId,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectAgentSkillResources);
        DbHelpers.Add(command, "skill_id", skillId);
        return await DbHelpers.ReadListAsync(
            command,
            static reader => new AgentSkillResourceDefinition
            {
                Name = reader.GetString(0),
                Description = DbHelpers.GetNullableString(reader, 1),
                MediaType = reader.GetString(2),
                Content = reader.GetString(3),
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<AgentSkillScriptDefinition>> ReadScriptsAsync(
        Guid skillId,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectAgentSkillScripts);
        DbHelpers.Add(command, "skill_id", skillId);
        return await DbHelpers.ReadListAsync(
            command,
            static reader => new AgentSkillScriptDefinition
            {
                Name = reader.GetString(0),
                Description = DbHelpers.GetNullableString(reader, 1),
                Extension = reader.GetString(2),
                Content = reader.GetString(3),
                ParametersSchema = DbHelpers.GetNullableString(reader, 4),
            },
            cancellationToken).ConfigureAwait(false);
    }

    private void ConfigureSkill(DbCommand command, AgentSkillDefinition skill, DateTimeOffset now)
    {
        DbHelpers.Add(command, "id", skill.Id == Guid.Empty ? AgentPrismId.NewId(now) : skill.Id);
        DbHelpers.Add(command, "tenant_id", skill.TenantId);
        DbHelpers.Add(command, "name", skill.Name);
        Dialect.AddText(command, "description", skill.Description);
        DbHelpers.Add(command, "instructions", skill.Instructions);
        Dialect.AddText(command, "compatibility", skill.Compatibility);
        Dialect.AddText(command, "license", skill.License);
        Dialect.AddText(command, "allowed_tools", skill.AllowedTools);
        Dialect.AddJsonb(command, "metadata", JsonSerializer.Serialize(
                new Dictionary<string, JsonElement>(skill.Metadata, StringComparer.Ordinal),
                AgentPrismJsonContext.Default.DictionaryStringJsonElement));
        DbHelpers.Add(command, "enabled", skill.Enabled);
        Dialect.AddTimestamp(command, "now", now);
    }

    private static AgentSkillDefinition ReadSkill(DbDataReader reader)
    {
        var metadata = JsonSerializer.Deserialize(
            reader.GetString(8),
            AgentPrismJsonContext.Default.DictionaryStringJsonElement)
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        return new AgentSkillDefinition
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = reader.GetString(3),
            Instructions = reader.GetString(4),
            Compatibility = DbHelpers.GetNullableString(reader, 5),
            License = DbHelpers.GetNullableString(reader, 6),
            AllowedTools = DbHelpers.GetNullableString(reader, 7),
            Metadata = metadata,
            Enabled = reader.GetBoolean(9),
            Version = reader.GetInt32(10),
            CreatedAt = DbHelpers.GetTimestamp(reader, 11),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 12),
        };
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);
}
