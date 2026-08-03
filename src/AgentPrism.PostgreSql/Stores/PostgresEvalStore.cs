using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Eval takimlarini, vakalarini ve kosularini PostgreSQL'de saklayan tenant-yalitimli depo.</summary>
public sealed class PostgresEvalStore : IEvalStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;

    /// <summary>Yeni bir eval deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    public PostgresEvalStore(NpgsqlDataSource dataSource, IOptions<AgentPrismPostgreSqlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectEvalSuites);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadSuite, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalSuite?> GetSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.SelectEvalSuite);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadSuite, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);

        var now = DateTimeOffset.UtcNow;
        var command = CreateCommand(_sql.UpsertEvalSuite);
        command.Parameters.AddWithValue("id", suite.Id == Guid.Empty ? AgentPrismId.NewId(now) : suite.Id);
        command.Parameters.AddWithValue("tenant_id", suite.TenantId);
        command.Parameters.AddWithValue("name", suite.Name);
        AddNullableText(command, "description", suite.Description);
        command.Parameters.AddWithValue("agent_name", suite.AgentName);
        command.Parameters.Add(new NpgsqlParameter("checks", NpgsqlDbType.Jsonb) { Value = RawJson(suite.Checks) });
        command.Parameters.AddWithValue("now", now.UtcDateTime);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadSuite, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException($"'{suite.Name}' eval takimi kaydedilemedi.");
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteSuiteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);

        var command = CreateCommand(_sql.DeleteEvalSuite);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("name", name);

        return await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(
        Guid suiteId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectEvalCases);
        command.Parameters.AddWithValue("suite_id", suiteId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadCase, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(
        Guid suiteId,
        IReadOnlyList<EvalCase> cases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var delete = new NpgsqlCommand(_sql.DeleteEvalCases, connection, transaction)
                {
                    CommandTimeout = _commandTimeout,
                };
                delete.Parameters.AddWithValue("suite_id", suiteId);
                await NpgsqlHelpers.ExecuteAsync(delete, cancellationToken).ConfigureAwait(false);

                var assigned = new List<EvalCase>(cases.Count);

                for (var seq = 0; seq < cases.Count; seq++)
                {
                    var candidate = cases[seq] with
                    {
                        Id = cases[seq].Id == Guid.Empty ? AgentPrismId.NewId() : cases[seq].Id,
                        SuiteId = suiteId,
                        Seq = seq,
                    };

                    var insert = new NpgsqlCommand(_sql.InsertEvalCase, connection, transaction)
                    {
                        CommandTimeout = _commandTimeout,
                    };
                    insert.Parameters.AddWithValue("id", candidate.Id);
                    insert.Parameters.AddWithValue("suite_id", candidate.SuiteId);
                    insert.Parameters.AddWithValue("seq", candidate.Seq);
                    insert.Parameters.AddWithValue("query", candidate.Query);
                    AddNullableText(insert, "expected_output", candidate.ExpectedOutput);
                    AddNullableText(
                        insert,
                        "expected_tools",
                        candidate.ExpectedTools.Count > 0 ? string.Join(',', candidate.ExpectedTools) : null);
                    AddNullableText(insert, "context", candidate.Context);
                    await NpgsqlHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);

                    assigned.Add(candidate);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return assigned;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var command = CreateCommand(_sql.InsertEvalRun);
        command.Parameters.AddWithValue("id", run.Id == Guid.Empty ? AgentPrismId.NewId() : run.Id);
        command.Parameters.AddWithValue("tenant_id", run.TenantId);
        command.Parameters.AddWithValue("suite_id", run.SuiteId);
        AddNullableUuid(command, "job_id", run.JobId);
        command.Parameters.AddWithValue("total", run.Total);
        command.Parameters.AddWithValue("started_at", run.StartedAt.UtcDateTime);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException("Eval kosu kaydi olusturulamadi.");
    }

    /// <inheritdoc />
    public async ValueTask MarkRunRunningAsync(
        Guid evalRunId,
        int? agentVersion,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.MarkEvalRunRunning);
        command.Parameters.AddWithValue("id", evalRunId);
        command.Parameters.Add(new NpgsqlParameter("agent_version", NpgsqlDbType.Integer)
        {
            Value = agentVersion.HasValue ? (object)agentVersion.Value : DBNull.Value,
        });
        AddNullableText(command, "model_id", modelId);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.CompleteEvalRun);
        command.Parameters.AddWithValue("id", completion.EvalRunId);
        command.Parameters.AddWithValue("status", (short)completion.Status);
        command.Parameters.AddWithValue("completed_at", completion.CompletedAt.UtcDateTime);
        command.Parameters.AddWithValue("total", completion.Total);
        command.Parameters.AddWithValue("passed", completion.Passed);
        command.Parameters.AddWithValue("failed", completion.Failed);
        command.Parameters.Add(new NpgsqlParameter("input_tokens", NpgsqlDbType.Bigint)
        {
            Value = completion.InputTokens.HasValue ? (object)completion.InputTokens.Value : DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("output_tokens", NpgsqlDbType.Bigint)
        {
            Value = completion.OutputTokens.HasValue ? (object)completion.OutputTokens.Value : DBNull.Value,
        });

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalRun?> GetRunAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectEvalRun);
        command.Parameters.AddWithValue("id", evalRunId);
        command.Parameters.AddWithValue("tenant_id", tenantId);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalRun?> GetRunByJobIdAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectEvalRunByJobId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("job_id", jobId);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalRun>> QueryRunsAsync(
        EvalRunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectEvalRuns);
        AddNullableText(command, "tenant_id", query.TenantId);
        AddNullableUuid(command, "suite_id", query.SuiteId);
        command.Parameters.AddWithValue("skip", Math.Max(query.Skip, 0));
        command.Parameters.AddWithValue("take", Math.Max(query.Take, 0));

        return await NpgsqlHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var command = CreateCommand(_sql.InsertEvalCaseResult);
        command.Parameters.AddWithValue("id", result.Id == Guid.Empty ? AgentPrismId.NewId() : result.Id);
        command.Parameters.AddWithValue("eval_run_id", result.EvalRunId);
        command.Parameters.AddWithValue("case_id", result.CaseId);
        AddNullableUuid(command, "run_id", result.RunId);
        command.Parameters.AddWithValue("passed", result.Passed);
        AddNullableText(command, "output", result.Output);
        command.Parameters.Add(new NpgsqlParameter("scores", NpgsqlDbType.Jsonb) { Value = RawJson(result.Scores) });
        AddNullableText(command, "failure_reason", result.FailureReason);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectEvalCaseResults);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("eval_run_id", evalRunId);

        return await NpgsqlHelpers.ReadListAsync(command, ReadCaseResult, cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;
        return command;
    }

    private static EvalSuite ReadSuite(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = NpgsqlHelpers.GetNullableString(reader, 3),
            AgentName = reader.GetString(4),
            Checks = ReadJsonb(reader, 5),
            CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 6),
            UpdatedAt = NpgsqlHelpers.GetTimestamp(reader, 7),
        };

    private static EvalCase ReadCase(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            SuiteId = reader.GetGuid(1),
            Seq = reader.GetInt32(2),
            Query = reader.GetString(3),
            ExpectedOutput = NpgsqlHelpers.GetNullableString(reader, 4),
            ExpectedTools = NpgsqlHelpers.GetNullableString(reader, 5) is { Length: > 0 } tools
                ? tools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [],
            Context = NpgsqlHelpers.GetNullableString(reader, 6),
        };

    private static EvalRun ReadRun(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            SuiteId = reader.GetGuid(2),
            JobId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            AgentVersion = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            ModelId = NpgsqlHelpers.GetNullableString(reader, 5),
            Status = (EvalRunStatus)reader.GetInt16(6),
            Total = reader.GetInt32(7),
            Passed = reader.GetInt32(8),
            Failed = reader.GetInt32(9),
            InputTokens = reader.IsDBNull(10) ? null : reader.GetInt64(10),
            OutputTokens = reader.IsDBNull(11) ? null : reader.GetInt64(11),
            StartedAt = NpgsqlHelpers.GetTimestamp(reader, 12),
            CompletedAt = NpgsqlHelpers.GetNullableTimestamp(reader, 13),
        };

    private static EvalCaseResult ReadCaseResult(NpgsqlDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            EvalRunId = reader.GetGuid(1),
            CaseId = reader.GetGuid(2),
            RunId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            Passed = reader.GetBoolean(4),
            Output = NpgsqlHelpers.GetNullableString(reader, 5),
            Scores = ReadJsonb(reader, 6),
            FailureReason = NpgsqlHelpers.GetNullableString(reader, 7),
        };

    /// <summary>
    /// jsonb sutununu <see cref="JsonElement"/> olarak okur.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonDocument"/> birakildiginda kendi tamponunu geri verir ve
    /// icinden alinan <see cref="JsonElement"/> gecersizlesir; <c>Clone()</c>
    /// tamponu kopyalar ve degeri cagiranin omrunden bagimsiz kilar.
    /// </remarks>
    private static JsonElement ReadJsonb(NpgsqlDataReader reader, int ordinal)
    {
        using var document = JsonDocument.Parse(reader.GetString(ordinal));
        return document.RootElement.Clone();
    }

    private static string RawJson(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Undefined ? "null" : payload.GetRawText();

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
}
