using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// Calistirma kayitlarini ve olay akisini PostgreSQL'de saklayan depo.
/// </summary>
/// <remarks>
/// <para>
/// Olaylar <em>append-only</em>'dir (karar K-014). Sira numarasini
/// <see cref="RunEventWriter"/> uretir; depo yalnizca yazar.
/// </para>
/// <para>
/// Davranis sozlesmesi <see cref="InMemoryRunStore"/> ile birebir aynidir ve ortak
/// sozlesme testleriyle korunur. Tum islemler <see cref="ITenantContext.TenantId"/>
/// ile sinirlidir.
/// </para>
/// </remarks>
public sealed class PostgresRunStore : IRunStore
{
    /// <summary>Yabanci anahtar ihlali SQLSTATE kodu.</summary>
    private const string ForeignKeyViolation = "23503";

    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly ITenantContext _tenantContext;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir calistirma deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresRunStore(
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
    public async ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        var record = new RunRecord
        {
            Id = info.RunId,
            AgentName = info.AgentName,
            Status = RunStatus.Running,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId ?? _tenantContext.TenantId,
            SessionId = info.SessionId,
            IsStreaming = info.IsStreaming,
        };

        var command = CreateCommand(_sql.InsertRun);
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("tenant_id", record.TenantId!);
        command.Parameters.AddWithValue("agent_name", record.AgentName);
        AddNullableText(command, "session_id", record.SessionId);
        command.Parameters.AddWithValue("status", (short)record.Status);
        command.Parameters.AddWithValue("started_at", record.StartedAt.UtcDateTime);
        command.Parameters.AddWithValue("is_streaming", record.IsStreaming);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return record;
    }

    /// <inheritdoc />
    public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var command = CreateCommand(_sql.InsertRunEvent);
        command.Parameters.AddWithValue("run_id", runEvent.RunId);
        command.Parameters.AddWithValue("seq", runEvent.Sequence);
        command.Parameters.AddWithValue("type", (short)runEvent.Type);
        AddNullableText(command, "text", runEvent.Text);
        AddNullableText(command, "tool_name", runEvent.ToolName);
        AddNullableText(command, "tool_call_id", runEvent.ToolCallId);
        AddNullableText(command, "payload", runEvent.Payload);
        command.Parameters.AddWithValue("created_at", runEvent.Timestamp.UtcDateTime);

        try
        {
            await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, ForeignKeyViolation, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"'{runEvent.RunId}' kimlikli calistirma bulunamadi. " +
                "Olay eklemeden once StartRunAsync cagrilmalidir.",
                ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.UpdateRunCompletion);
        command.Parameters.AddWithValue("id", completion.RunId);
        command.Parameters.AddWithValue("status", (short)completion.Status);
        command.Parameters.AddWithValue("completed_at", completion.CompletedAt.UtcDateTime);
        command.Parameters.AddWithValue("event_count", completion.EventCount);
        AddNullableInt64(command, "input_tokens", completion.Usage?.InputTokens);
        AddNullableInt64(command, "output_tokens", completion.Usage?.OutputTokens);
        AddNullableInt64(command, "total_tokens", completion.Usage?.TotalTokens);
        AddNullableText(command, "error_type", completion.Error?.Type);
        AddNullableText(command, "error_message", completion.Error?.Message);

        var affected = await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new AgentPrismException($"'{completion.RunId}' kimlikli calistirma bulunamadi.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRun);
        command.Parameters.AddWithValue("id", runId);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectRuns);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? _tenantContext.TenantId);
        AddNullableText(command, "agent_name", query.AgentName);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Smallint)
        {
            Value = query.Status is { } status ? (object)(short)status : DBNull.Value,
        });
        AddNullableText(command, "session_id", query.SessionId);
        command.Parameters.Add(new NpgsqlParameter("started_after", NpgsqlDbType.TimestampTz)
        {
            Value = query.StartedAfter is { } after ? (object)after.UtcDateTime : DBNull.Value,
        });
        command.Parameters.AddWithValue("skip", Math.Max(query.Skip, 0));
        command.Parameters.AddWithValue("take", Math.Max(query.Take, 0));

        return await NpgsqlHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRunEvents);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("from_sequence", fromSequence);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return ReadEvent(reader);
                }
            }
        }
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

    private static RunEvent ReadEvent(NpgsqlDataReader reader)
        => new()
        {
            RunId = reader.GetGuid(0),
            Sequence = reader.GetInt64(1),
            Type = (RunEventType)reader.GetInt16(2),
            Text = NpgsqlHelpers.GetNullableString(reader, 3),
            ToolName = NpgsqlHelpers.GetNullableString(reader, 4),
            ToolCallId = NpgsqlHelpers.GetNullableString(reader, 5),
            Payload = NpgsqlHelpers.GetNullableString(reader, 6),
            Timestamp = NpgsqlHelpers.GetTimestamp(reader, 7),
        };

    private static RunRecord ReadRun(NpgsqlDataReader reader)
    {
        var errorType = NpgsqlHelpers.GetNullableString(reader, 12);

        return new RunRecord
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = reader.GetString(2),
            SessionId = NpgsqlHelpers.GetNullableString(reader, 3),
            Status = (RunStatus)reader.GetInt16(4),
            StartedAt = NpgsqlHelpers.GetTimestamp(reader, 5),
            CompletedAt = reader.IsDBNull(6) ? null : NpgsqlHelpers.GetTimestamp(reader, 6),
            IsStreaming = reader.GetBoolean(7),
            Usage = ReadUsage(reader),
            EventCount = reader.GetInt64(11),
            Error = errorType is null
                ? null
                : new RunError
                {
                    Type = errorType,
                    Message = NpgsqlHelpers.GetNullableString(reader, 13) ?? string.Empty,
                },
        };
    }

    private static RunUsage? ReadUsage(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(8) && reader.IsDBNull(9) && reader.IsDBNull(10))
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            OutputTokens = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            TotalTokens = reader.IsDBNull(10) ? null : reader.GetInt64(10),
        };
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });

    private static void AddNullableInt64(NpgsqlCommand command, string name, long? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bigint)
        {
            Value = value.HasValue ? (object)value.Value : DBNull.Value,
        });
}
