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
            Kind = info.Kind,
            WorkflowName = info.WorkflowName,
            Status = RunStatus.Running,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId ?? _tenantContext.TenantId,
            SessionId = info.SessionId,
            ModelId = info.ModelId,
            IsStreaming = info.IsStreaming,
            ParentRunId = info.ParentRunId,
            RootRunId = info.RootRunId,
            Depth = info.Depth,
            AgentVersion = info.AgentVersion,
            ExperimentId = info.ExperimentId,
            Variant = info.Variant,
        };

        var command = CreateCommand(_sql.InsertRun);
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("tenant_id", record.TenantId!);
        command.Parameters.AddWithValue("agent_name", record.AgentName);
        AddNullableText(command, "session_id", record.SessionId);
        AddNullableText(command, "model_id", record.ModelId);
        command.Parameters.AddWithValue("status", (short)record.Status);
        command.Parameters.AddWithValue("started_at", record.StartedAt.UtcDateTime);
        command.Parameters.AddWithValue("is_streaming", record.IsStreaming);
        AddNullableUuid(command, "parent_run_id", record.ParentRunId);
        AddNullableUuid(command, "root_run_id", record.RootRunId);
        command.Parameters.AddWithValue("kind", (short)record.Kind);
        AddNullableText(command, "workflow_name", record.WorkflowName);
        AddNullableInt32(command, "agent_version", record.AgentVersion);
        AddNullableUuid(command, "experiment_id", record.ExperimentId);
        AddNullableText(command, "variant", record.Variant);

        // Derinlik smallint sutunudur; kaynagi butcenin MaxDepth degeridir ve
        // hicbir kurulumda short sinirina yaklasmaz.
        command.Parameters.AddWithValue("depth", (short)Math.Clamp(record.Depth, 0, short.MaxValue));

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
        AddNullableDecimal(command, "input_cost", completion.Cost?.InputCost);
        AddNullableDecimal(command, "output_cost", completion.Cost?.OutputCost);
        AddNullableText(command, "cost_currency", completion.Cost?.Currency);
        command.Parameters.Add(new NpgsqlParameter("pricing_source", NpgsqlDbType.Smallint)
        {
            Value = completion.Cost is { } cost ? (object)(short)cost.Source : DBNull.Value,
        });

        var affected = await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new AgentPrismException($"'{completion.RunId}' kimlikli calistirma bulunamadi.");
        }
    }

    /// <inheritdoc />
    public async ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.UpdateRunCost);
        command.Parameters.AddWithValue("id", runId);
        AddNullableDecimal(command, "input_cost", cost?.InputCost);
        AddNullableDecimal(command, "output_cost", cost?.OutputCost);
        AddNullableText(command, "cost_currency", cost?.Currency);
        command.Parameters.Add(new NpgsqlParameter("pricing_source", NpgsqlDbType.Smallint)
        {
            Value = cost is { } value ? (object)(short)value.Source : DBNull.Value,
        });

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
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
        AddNullableUuid(command, "parent_run_id", query.ParentRunId);
        AddNullableUuid(command, "root_run_id", query.RootRunId);
        command.Parameters.AddWithValue("only_root_runs", query.OnlyRootRuns);
        command.Parameters.AddWithValue("skip", Math.Max(query.Skip, 0));
        command.Parameters.AddWithValue("take", Math.Max(query.Take, 0));

        return await NpgsqlHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectRunStatistics);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? _tenantContext.TenantId);
        AddNullableText(command, "agent_name", query.AgentName);
        command.Parameters.Add(new NpgsqlParameter("started_after", NpgsqlDbType.TimestampTz)
        {
            Value = query.StartedAfter is { } after ? (object)after.UtcDateTime : DBNull.Value,
        });
        command.Parameters.AddWithValue("max_agents", Math.Max(query.MaxAgents, 0));
        command.Parameters.AddWithValue("status_running", (short)RunStatus.Running);
        command.Parameters.AddWithValue("status_completed", (short)RunStatus.Completed);
        command.Parameters.AddWithValue("status_failed", (short)RunStatus.Failed);
        command.Parameters.AddWithValue("status_canceled", (short)RunStatus.Canceled);
        command.Parameters.AddWithValue("status_awaiting", (short)RunStatus.AwaitingInput);
        command.Parameters.AddWithValue("kind_eval", (short)RunKind.Eval);
        command.Parameters.AddWithValue("pricing_source_unknown", (short)PricingSource.Unknown);

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                // Birinci sonuc kumesi: toplam ozet. Toplama sorgusu her zaman
                // tam olarak bir satir dondurur.
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return EmptyStatistics;
                }

                var total = reader.GetInt64(0);
                var completed = reader.GetInt64(1);
                var failed = reader.GetInt64(2);
                var canceled = reader.GetInt64(3);
                var running = reader.GetInt64(4);
                var awaitingInput = reader.GetInt64(5);
                var inputTokens = reader.GetInt64(6);
                var outputTokens = reader.GetInt64(7);
                var totalTokens = reader.GetInt64(8);
                var totalCost = NpgsqlHelpers.GetNullableDecimal(reader, 9);
                var currency = NpgsqlHelpers.GetNullableString(reader, 10);
                var runsWithUnknownPricing = reader.GetInt64(11);

                // Ikinci sonuc kumesi: agent kirilimi.
                var byAgent = new List<RunAgentStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byAgent.Add(new RunAgentStatistics
                        {
                            AgentName = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            FailedRuns = reader.GetInt64(2),
                            TotalTokens = reader.GetInt64(3),
                        });
                    }
                }

                // Ucuncu sonuc kumesi: model kirilimi. model_id NULL olan
                // calistirmalar sorguda elenir; toplamlarda ise sayilirlar.
                var byModel = new List<RunModelStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byModel.Add(new RunModelStatistics
                        {
                            ModelId = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            InputTokens = reader.GetInt64(2),
                            OutputTokens = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                            TotalCost = NpgsqlHelpers.GetNullableDecimal(reader, 5),
                        });
                    }
                }

                // Dorduncu sonuc kumesi: surum kirilimi. agent_version NULL olan
                // calistirmalar sorguda elenir; toplamlarda ise sayilirlar.
                var byVersion = new List<RunVersionStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byVersion.Add(new RunVersionStatistics
                        {
                            AgentName = reader.GetString(0),
                            Version = reader.GetInt32(1),
                            TotalRuns = reader.GetInt64(2),
                            FailedRuns = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                        });
                    }
                }

                return new RunStatistics
                {
                    TotalRuns = total,
                    CompletedRuns = completed,
                    FailedRuns = failed,
                    CanceledRuns = canceled,
                    RunningRuns = running,
                    AwaitingInputRuns = awaitingInput,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = totalTokens,
                    TotalCost = totalCost,
                    Currency = currency,
                    RunsWithUnknownPricing = runsWithUnknownPricing,
                    ByAgent = byAgent,
                    ByModel = byModel,
                    ByVersion = byVersion,
                };
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        RunTimeSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        RunTimeSeriesBucketing.Validate(query.From, query.To, query.Bucket);

        var command = CreateCommand(_sql.SelectRunTimeSeries);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? _tenantContext.TenantId);
        command.Parameters.AddWithValue("from_ts", query.From.UtcDateTime);
        command.Parameters.AddWithValue("to_ts", query.To.UtcDateTime);
        command.Parameters.AddWithValue("bucket_unit", query.Bucket == TimeSeriesBucket.Hour ? "hour" : "day");
        command.Parameters.Add(new NpgsqlParameter("bucket_step", NpgsqlDbType.Interval)
        {
            Value = RunTimeSeriesBucketing.StepFor(query.Bucket),
        });
        command.Parameters.AddWithValue("status_failed", (short)RunStatus.Failed);
        AddNullableText(command, "agent_name", query.AgentName);
        AddNullableText(command, "model_id", query.ModelId);
        command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Smallint)
        {
            Value = query.Kind is { } kind ? (object)(short)kind : DBNull.Value,
        });

        return await NpgsqlHelpers.ReadListAsync(command, ReadTimeSeriesPoint, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var command = CreateCommand(_sql.InsertToolInvocation);
        command.Parameters.AddWithValue("id", invocation.Id);
        command.Parameters.AddWithValue("run_id", invocation.RunId);
        command.Parameters.AddWithValue("tool_name", invocation.ToolName);
        AddNullableText(command, "tool_call_id", invocation.ToolCallId);
        AddNullableText(command, "source", invocation.Source);
        AddNullableText(command, "arguments", invocation.Arguments);
        AddNullableText(command, "result", invocation.Result);
        command.Parameters.Add(new NpgsqlParameter("duration_ms", NpgsqlDbType.Integer)
        {
            // Sure `integer` sutununda milisaniye olarak saklanir; 24 gunden
            // uzun bir tool cagrisi gercekci degildir ve tasma olusmaz.
            Value = invocation.Duration is { } duration
                ? (object)(int)Math.Clamp(duration.TotalMilliseconds, 0, int.MaxValue)
                : DBNull.Value,
        });
        AddNullableText(command, "error", invocation.Error);
        command.Parameters.AddWithValue("created_at", invocation.CreatedAt.UtcDateTime);

        try
        {
            await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, ForeignKeyViolation, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"'{invocation.RunId}' kimlikli calistirma bulunamadi. " +
                "Tool cagrisi kaydetmeden once StartRunAsync cagrilmalidir.",
                ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectToolInvocations);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        return await NpgsqlHelpers
            .ReadListAsync(command, ReadToolInvocation, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
        ToolUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectToolUsage);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? _tenantContext.TenantId);
        command.Parameters.Add(new NpgsqlParameter("started_after", NpgsqlDbType.TimestampTz)
        {
            Value = query.StartedAfter is { } after ? (object)after.UtcDateTime : DBNull.Value,
        });
        command.Parameters.AddWithValue("max_tools", Math.Max(query.MaxTools, 0));

        return await NpgsqlHelpers
            .ReadListAsync(command, ReadToolUsage, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectExperimentResults);
        command.Parameters.AddWithValue("tenant_id", query.TenantId ?? _tenantContext.TenantId);
        command.Parameters.AddWithValue("experiment_id", query.ExperimentId);
        command.Parameters.AddWithValue("status_completed", (short)RunStatus.Completed);
        command.Parameters.AddWithValue("status_failed", (short)RunStatus.Failed);
        command.Parameters.AddWithValue("status_canceled", (short)RunStatus.Canceled);

        return await NpgsqlHelpers
            .ReadListAsync(command, ReadExperimentVariantResult, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Hic satir donmeyen ozet sorgusu icin notr sonuc.</summary>
    private static RunStatistics EmptyStatistics { get; } = new()
    {
        TotalRuns = 0,
        CompletedRuns = 0,
        FailedRuns = 0,
        CanceledRuns = 0,
        RunningRuns = 0,
    };

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
        var ownUsage = ReadUsage(reader);
        var ownCost = ReadCost(reader);

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
            Usage = ownUsage,
            EventCount = reader.GetInt64(11),
            ModelId = NpgsqlHelpers.GetNullableString(reader, 14),
            ParentRunId = reader.IsDBNull(15) ? null : reader.GetGuid(15),
            RootRunId = reader.IsDBNull(16) ? null : reader.GetGuid(16),
            Depth = reader.GetInt16(17),
            ChildRunCount = reader.IsDBNull(18) ? 0 : reader.GetInt32(18),
            TreeUsage = ReadTreeUsage(reader, ownUsage),
            Kind = (RunKind)reader.GetInt16(23),
            WorkflowName = NpgsqlHelpers.GetNullableString(reader, 24),
            AgentVersion = reader.IsDBNull(25) ? null : reader.GetInt32(25),
            ExperimentId = reader.IsDBNull(26) ? null : reader.GetGuid(26),
            Variant = NpgsqlHelpers.GetNullableString(reader, 27),
            Cost = ownCost,
            TreeCost = ReadTreeCost(reader, ownCost),
            Error = errorType is null
                ? null
                : new RunError
                {
                    Type = errorType,
                    Message = NpgsqlHelpers.GetNullableString(reader, 13) ?? string.Empty,
                },
        };
    }

    /// <summary>Agacin token toplamini kaydin kendi kullanimiyla birlestirir.</summary>
    /// <remarks>
    /// Alt sorgu yalnizca <em>altindaki</em> calistirmalari toplar; kaydin kendi
    /// kullanimi buraya eklenir. Ne kayitta ne agacta kullanim varsa deger
    /// <see langword="null"/> kalir: sifir yazmak, "saglayici token bildirmedi"
    /// ile "hic token harcanmadi" durumlarini ayirt edilemez hale getirirdi.
    /// </remarks>
    private static RunUsage? ReadTreeUsage(NpgsqlDataReader reader, RunUsage? ownUsage)
    {
        var descendantRows = reader.IsDBNull(22) ? 0 : reader.GetInt64(22);

        if (descendantRows == 0 && ownUsage is null)
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = (reader.IsDBNull(19) ? 0 : reader.GetInt64(19)) + (ownUsage?.InputTokens ?? 0),
            OutputTokens = (reader.IsDBNull(20) ? 0 : reader.GetInt64(20)) + (ownUsage?.OutputTokens ?? 0),
            TotalTokens = (reader.IsDBNull(21) ? 0 : reader.GetInt64(21)) + (ownUsage?.TotalTokens ?? 0),
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

    /// <summary>
    /// Kaydin kendi maliyetini okur. <c>pricing_source</c> NULL ise model hic
    /// bilinmiyordu demektir (<see cref="PricingSource.Unknown"/>'dan farkli):
    /// bu durumda <see langword="null"/> doner.
    /// </summary>
    private static RunCost? ReadCost(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(31))
        {
            return null;
        }

        return new RunCost
        {
            InputCost = NpgsqlHelpers.GetNullableDecimal(reader, 28),
            OutputCost = NpgsqlHelpers.GetNullableDecimal(reader, 29),
            Currency = NpgsqlHelpers.GetNullableString(reader, 30),
            Source = (PricingSource)reader.GetInt16(31),
        };
    }

    /// <summary>Agacin maliyet toplamini kaydin kendi maliyetiyle birlestirir.</summary>
    /// <remarks>Ayni gerekce <see cref="ReadTreeUsage"/> ile.</remarks>
    private static RunTreeCost? ReadTreeCost(NpgsqlDataReader reader, RunCost? ownCost)
    {
        var pricedDescendants = reader.IsDBNull(36) ? 0 : reader.GetInt64(36);

        if (pricedDescendants == 0 && ownCost is null)
        {
            return null;
        }

        var inputCost = NpgsqlHelpers.GetNullableDecimal(reader, 32);
        var outputCost = NpgsqlHelpers.GetNullableDecimal(reader, 33);

        if (ownCost?.InputCost is { } ownInput)
        {
            inputCost = (inputCost ?? 0) + ownInput;
        }

        if (ownCost?.OutputCost is { } ownOutput)
        {
            outputCost = (outputCost ?? 0) + ownOutput;
        }

        var unknownPricing = (reader.IsDBNull(35) ? 0 : reader.GetInt64(35))
            + (ownCost?.Source == PricingSource.Unknown ? 1 : 0);

        return new RunTreeCost
        {
            InputCost = inputCost,
            OutputCost = outputCost,
            Currency = NpgsqlHelpers.GetNullableString(reader, 34) ?? ownCost?.Currency,
            RunsWithUnknownPricing = unknownPricing,
        };
    }

    private static ToolInvocationRecord ReadToolInvocation(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            ToolName = reader.GetString(2),
            ToolCallId = NpgsqlHelpers.GetNullableString(reader, 3),
            Source = NpgsqlHelpers.GetNullableString(reader, 4),
            Arguments = NpgsqlHelpers.GetNullableString(reader, 5),
            Result = NpgsqlHelpers.GetNullableString(reader, 6),
            Duration = reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(7)),
            Error = NpgsqlHelpers.GetNullableString(reader, 8),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 9),
        };

    private static ToolUsage ReadToolUsage(NpgsqlDataReader reader)
        => new()
        {
            ToolName = reader.GetString(0),
            TotalCalls = reader.GetInt64(1),
            FailedCalls = reader.GetInt64(2),
            AverageDurationMs = reader.IsDBNull(3) ? null : reader.GetDouble(3),
            LastCalledAt = reader.IsDBNull(4) ? null : NpgsqlHelpers.GetTimestamp(reader, 4),
        };

    private static TimeSeriesPoint ReadTimeSeriesPoint(NpgsqlDataReader reader)
        => new()
        {
            Bucket = NpgsqlHelpers.GetTimestamp(reader, 0),
            Runs = reader.GetInt64(1),
            FailedRuns = reader.GetInt64(2),
            InputTokens = reader.GetInt64(3),
            OutputTokens = reader.GetInt64(4),
            Cost = NpgsqlHelpers.GetNullableDecimal(reader, 5),
            AverageDurationMs = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        };

    private static ExperimentVariantResult ReadExperimentVariantResult(NpgsqlDataReader reader)
        => new()
        {
            Variant = reader.GetString(0),
            Version = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            TotalRuns = reader.GetInt64(2),
            CompletedRuns = reader.GetInt64(3),
            FailedRuns = reader.GetInt64(4),
            CanceledRuns = reader.GetInt64(5),
            InputTokens = reader.GetInt64(6),
            OutputTokens = reader.GetInt64(7),
            TotalTokens = reader.GetInt64(8),
            AverageDurationMs = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            TotalCost = NpgsqlHelpers.GetNullableDecimal(reader, 10),
            Currency = NpgsqlHelpers.GetNullableString(reader, 11),
        };

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value,
        });

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        {
            Value = value.HasValue ? (object)value.Value : DBNull.Value,
        });

    private static void AddNullableInt64(NpgsqlCommand command, string name, long? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bigint)
        {
            Value = value.HasValue ? (object)value.Value : DBNull.Value,
        });

    private static void AddNullableInt32(NpgsqlCommand command, string name, int? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Integer)
        {
            Value = value.HasValue ? (object)value.Value : DBNull.Value,
        });

    private static void AddNullableDecimal(NpgsqlCommand command, string name, decimal? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Numeric)
        {
            Value = value.HasValue ? (object)value.Value : DBNull.Value,
        });
}
