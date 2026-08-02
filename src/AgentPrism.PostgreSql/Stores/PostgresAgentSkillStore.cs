using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Skill tanimlarini PostgreSQL'de saklayan tenant-yalitimli depo.</summary>
public sealed class PostgresAgentSkillStore : IAgentSkillStore
{
   private readonly NpgsqlDataSource _dataSource;
   private readonly SqlQueries _sql;
   private readonly int _commandTimeout;

   /// <summary>Yeni bir skill deposu olusturur.</summary>
   /// <param name="dataSource">Veri kaynagi.</param>
   /// <param name="options">PostgreSQL ayarlari.</param>
   public PostgresAgentSkillStore(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
   {
      ArgumentNullException.ThrowIfNull(dataSource);
      ArgumentNullException.ThrowIfNull(options);

      _dataSource = dataSource;
      _sql = new SqlQueries(options.Value.SchemaName);
      _commandTimeout = options.Value.CommandTimeoutSeconds;
   }

   /// <inheritdoc />
   public async ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
       string tenantId,
       CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(tenantId);

      var command = CreateCommand(_sql.SelectAgentSkills);
      command.Parameters.AddWithValue("tenant_id", tenantId);
      var rows = await NpgsqlHelpers.ReadListAsync(command, ReadSkill, cancellationToken).ConfigureAwait(false);

      for (var index = 0; index < rows.Count; index++)
      {
         var skill = rows[index];
         rows[index] = skill with
         {
            Resources = await ReadResourcesAsync(skill.Id, cancellationToken).ConfigureAwait(false),
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
      command.Parameters.AddWithValue("tenant_id", tenantId);
      command.Parameters.AddWithValue("name", name);
      var skill = await NpgsqlHelpers.ReadSingleAsync(command, ReadSkill, cancellationToken).ConfigureAwait(false);

      if (skill is { })
      {
         return skill with
         {
            Resources = await ReadResourcesAsync(skill.Id, cancellationToken).ConfigureAwait(false),
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
      var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
      await using (connection.ConfigureAwait(false))
      {
         var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
         await using (transaction.ConfigureAwait(false))
         {
            var upsert = new NpgsqlCommand(_sql.UpsertAgentSkill, connection, transaction)
            {
               CommandTimeout = _commandTimeout,
            };
            ConfigureSkill(upsert, skill, now);
            var saved = await NpgsqlHelpers.ReadSingleAsync(upsert, ReadSkill, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException($"'{skill.Name}' skill'i kaydedilemedi.");

            var deleteResources = new NpgsqlCommand(_sql.DeleteAgentSkillResources, connection, transaction)
            {
               CommandTimeout = _commandTimeout,
            };
            deleteResources.Parameters.AddWithValue("skill_id", saved.Id);
            await NpgsqlHelpers.ExecuteAsync(deleteResources, cancellationToken).ConfigureAwait(false);

            foreach (var resource in skill.Resources)
            {
               var insertResource = new NpgsqlCommand(_sql.InsertAgentSkillResource, connection, transaction)
               {
                  CommandTimeout = _commandTimeout,
               };
               insertResource.Parameters.AddWithValue("id", AgentPrismId.NewId());
               insertResource.Parameters.AddWithValue("skill_id", saved.Id);
               insertResource.Parameters.AddWithValue("name", resource.Name);
               insertResource.Parameters.AddWithValue("description", (object?)resource.Description ?? DBNull.Value);
               insertResource.Parameters.AddWithValue("media_type", resource.MediaType);
               insertResource.Parameters.AddWithValue("content", resource.Content);
               insertResource.Parameters.AddWithValue("created_at", now.UtcDateTime);
               await NpgsqlHelpers.ExecuteAsync(insertResource, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return saved with { Resources = skill.Resources };
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
      command.Parameters.AddWithValue("tenant_id", tenantId);
      command.Parameters.AddWithValue("name", name);
      return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
   }

   private async ValueTask<IReadOnlyList<AgentSkillResourceDefinition>> ReadResourcesAsync(
       Guid skillId,
       CancellationToken cancellationToken)
   {
      var command = CreateCommand(_sql.SelectAgentSkillResources);
      command.Parameters.AddWithValue("skill_id", skillId);
      return await NpgsqlHelpers.ReadListAsync(
          command,
          static reader => new AgentSkillResourceDefinition
          {
             Name = reader.GetString(0),
             Description = NpgsqlHelpers.GetNullableString(reader, 1),
             MediaType = reader.GetString(2),
             Content = reader.GetString(3),
          },
          cancellationToken).ConfigureAwait(false);
   }

   private static void ConfigureSkill(NpgsqlCommand command, AgentSkillDefinition skill, DateTimeOffset now)
   {
      command.Parameters.AddWithValue("id", skill.Id == Guid.Empty ? AgentPrismId.NewId(now) : skill.Id);
      command.Parameters.AddWithValue("tenant_id", skill.TenantId);
      command.Parameters.AddWithValue("name", skill.Name);
      command.Parameters.AddWithValue("description", skill.Description);
      command.Parameters.AddWithValue("instructions", skill.Instructions);
      command.Parameters.AddWithValue("compatibility", (object?)skill.Compatibility ?? DBNull.Value);
      command.Parameters.AddWithValue("license", (object?)skill.License ?? DBNull.Value);
      command.Parameters.AddWithValue("allowed_tools", (object?)skill.AllowedTools ?? DBNull.Value);
      command.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Jsonb)
      {
         Value = JsonSerializer.Serialize(
              new Dictionary<string, JsonElement>(skill.Metadata, StringComparer.Ordinal),
              AgentPrismJsonContext.Default.DictionaryStringJsonElement),
      });
      command.Parameters.AddWithValue("enabled", skill.Enabled);
      command.Parameters.AddWithValue("now", now.UtcDateTime);
   }

   private static AgentSkillDefinition ReadSkill(NpgsqlDataReader reader)
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
         Compatibility = NpgsqlHelpers.GetNullableString(reader, 5),
         License = NpgsqlHelpers.GetNullableString(reader, 6),
         AllowedTools = NpgsqlHelpers.GetNullableString(reader, 7),
         Metadata = metadata,
         Enabled = reader.GetBoolean(9),
         Version = reader.GetInt32(10),
         CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 11),
         UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 12),
      };
   }

   private NpgsqlCommand CreateCommand(string sql)
   {
      var command = _dataSource.CreateCommand(sql);
      command.CommandTimeout = _commandTimeout;
      return command;
   }
}
