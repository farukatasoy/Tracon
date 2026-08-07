using System.Data.Common;
using System.Text.Json;

namespace AgentPrism;

/// <summary>Eval takimlarini, vakalarini ve kosularini PostgreSQL'de saklayan tenant-yalitimli depo.</summary>
internal sealed class SqlEvalStore : IEvalStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir eval deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    public SqlEvalStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectEvalSuites);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadListAsync(command, ReadSuite, cancellationToken).ConfigureAwait(false);
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
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ReadSingleAsync(command, ReadSuite, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);

        var now = DateTimeOffset.UtcNow;
        var command = CreateCommand(_sql.UpsertEvalSuite);
        DbHelpers.Add(command, "id", suite.Id == Guid.Empty ? AgentPrismId.NewId(now) : suite.Id);
        DbHelpers.Add(command, "tenant_id", suite.TenantId);
        DbHelpers.Add(command, "name", suite.Name);
        AddNullableText(command, "description", suite.Description);
        DbHelpers.Add(command, "agent_name", suite.AgentName);
        Dialect.AddJsonb(command, "checks", RawJson(suite.Checks));
        Dialect.AddTimestamp(command, "now", now);

        return await DbHelpers.ReadSingleAsync(command, ReadSuite, cancellationToken).ConfigureAwait(false)
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
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "name", name);

        return await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Vakalar takimin ALTINDA yasar; takim kimligi kiraciya suzulmus bir sorgudan (ListSuitesAsync/GetSuiteAsync) gelir ve kendisi bir kiraci niyeti tasimaz.")]
    public async ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(
        Guid suiteId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectEvalCases);
        DbHelpers.Add(command, "suite_id", suiteId);

        return await DbHelpers.ReadListAsync(command, ReadCase, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "ListCasesAsync ile ayni gerekce: takim kimligi kiraciya suzulmus bir sorgudan gelir.")]
    public async ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(
        Guid suiteId,
        IReadOnlyList<EvalCase> cases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var delete = _context.CreateCommand(_sql.DeleteEvalCases, connection, transaction);
                DbHelpers.Add(delete, "suite_id", suiteId);
                await DbHelpers.ExecuteAsync(delete, cancellationToken).ConfigureAwait(false);

                var assigned = new List<EvalCase>(cases.Count);

                for (var seq = 0; seq < cases.Count; seq++)
                {
                    var candidate = cases[seq] with
                    {
                        Id = cases[seq].Id == Guid.Empty ? AgentPrismId.NewId() : cases[seq].Id,
                        SuiteId = suiteId,
                        Seq = seq,
                    };

                    var insert = _context.CreateCommand(_sql.InsertEvalCase, connection, transaction);
                    DbHelpers.Add(insert, "id", candidate.Id);
                    DbHelpers.Add(insert, "suite_id", candidate.SuiteId);
                    DbHelpers.Add(insert, "seq", candidate.Seq);
                    DbHelpers.Add(insert, "query", candidate.Query);
                    AddNullableText(insert, "expected_output", candidate.ExpectedOutput);
                    AddNullableText(
                        insert,
                        "expected_tools",
                        candidate.ExpectedTools.Count > 0 ? string.Join(',', candidate.ExpectedTools) : null);
                    AddNullableText(insert, "context", candidate.Context);
                    await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);

                    assigned.Add(candidate);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return assigned;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 <c>seq</c> depo icinde <c>MAX(seq) + 1</c> alt sorgusuyla atomik
    /// hesaplanir; iki es zamanli terfi ayni degeri hesaplayabilir. Bu durumda
    /// <c>eval_cases_suite_seq_uq</c> ihlali <see cref="SqlDialect.IsUniqueViolation"/>
    /// ile yakalanir ve YENIDEN DENENIR. Ayni <c>SourceRunId</c>'nin ikinci kez
    /// eklenmeye calisilmasi da benzersizlik ihlaline duser (<c>eval_cases_source_run_uq</c>)
    /// ama farkli yorumlanir: mevcut vaka <c>SelectEvalCaseBySourceRun</c> ile
    /// okunup <c>Created: false</c> ile donulur (docs/45-URETIMDEN-EVAL-KUMESI.md,
    /// bolum 45.2).
    /// </remarks>
    [TenantAgnostic(
        "ReplaceCasesAsync ile ayni gerekce: takim kimligi kiraciya suzulmus bir sorgudan gelir.")]
    public async ValueTask<EvalCaseAddResult> AddCaseAsync(
        Guid suiteId,
        EvalCaseDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        const int maxAttempts = 5;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var now = DateTimeOffset.UtcNow;
            var command = CreateCommand(_sql.InsertEvalCaseWithComputedSeq);
            DbHelpers.Add(command, "id", AgentPrismId.NewId(now));
            DbHelpers.Add(command, "suite_id", suiteId);
            DbHelpers.Add(command, "query", draft.Query);
            AddNullableText(command, "expected_output", draft.ExpectedOutput);
            AddNullableText(
                command,
                "expected_tools",
                draft.ExpectedTools.Count > 0 ? string.Join(',', draft.ExpectedTools) : null);
            AddNullableText(command, "context", draft.Context);
            AddNullableUuid(command, "source_run_id", draft.SourceRunId);
            Dialect.AddInt16(command, "source_kind", draft.SourceKind is { } kind ? (short)kind : null);
            Dialect.AddTimestamp(command, "promoted_at", now);

            try
            {
                var inserted = await DbHelpers.ReadSingleAsync(command, ReadCase, cancellationToken).ConfigureAwait(false)
                    ?? throw new AgentPrismException("Eval vakasi eklenemedi.");

                return new EvalCaseAddResult { Case = inserted, Created = true };
            }
            catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
            {
                if (draft.SourceRunId is { } sourceRunId)
                {
                    var existing = await GetCaseBySourceRunAsync(suiteId, sourceRunId, cancellationToken).ConfigureAwait(false);

                    if (existing is not null)
                    {
                        return new EvalCaseAddResult { Case = existing, Created = false };
                    }
                }

                // Ihlal source_run_id'den degilse seq catismasidir: bir sonraki
                // denemede MAX(seq) yeniden okunur ve taze bir deger uretilir.
            }
        }

        throw new AgentPrismException(
            $"Eval vakasi eklenemedi: {maxAttempts} denemede sira numarasi atanamadi (cok fazla es zamanli terfi).");
    }

    private async ValueTask<EvalCase?> GetCaseBySourceRunAsync(
        Guid suiteId, Guid sourceRunId, CancellationToken cancellationToken)
    {
        var command = CreateCommand(_sql.SelectEvalCaseBySourceRun);
        DbHelpers.Add(command, "suite_id", suiteId);
        DbHelpers.Add(command, "source_run_id", sourceRunId);

        return await DbHelpers.ReadSingleAsync(command, ReadCase, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var command = CreateCommand(_sql.InsertEvalRun);
        DbHelpers.Add(command, "id", run.Id == Guid.Empty ? AgentPrismId.NewId() : run.Id);
        DbHelpers.Add(command, "tenant_id", run.TenantId);
        DbHelpers.Add(command, "suite_id", run.SuiteId);
        AddNullableUuid(command, "job_id", run.JobId);
        DbHelpers.Add(command, "total", run.Total);
        Dialect.AddTimestamp(command, "started_at", run.StartedAt);

        return await DbHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismException("Eval kosu kaydi olusturulamadi.");
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Kosu kimligi kosuyu ACAN kod tarafindan uretilir (uuid v7); cagride ayri bir kiraci niyeti yoktur. Kiraci siniri kosu okumalarinda zorlanir.")]
    public async ValueTask MarkRunRunningAsync(
        Guid evalRunId,
        int? agentVersion,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.MarkEvalRunRunning);
        DbHelpers.Add(command, "id", evalRunId);
        Dialect.AddInt32(command, "agent_version", agentVersion);
        AddNullableText(command, "model_id", modelId);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "MarkRunRunningAsync ile ayni gerekce: kiraci kosudan miras alinir.")]
    public async ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.CompleteEvalRun);
        DbHelpers.Add(command, "id", completion.EvalRunId);
        DbHelpers.Add(command, "status", (short)completion.Status);
        Dialect.AddTimestamp(command, "completed_at", completion.CompletedAt);
        DbHelpers.Add(command, "total", completion.Total);
        DbHelpers.Add(command, "passed", completion.Passed);
        DbHelpers.Add(command, "failed", completion.Failed);
        Dialect.AddInt64(command, "input_tokens", completion.InputTokens);
        Dialect.AddInt64(command, "output_tokens", completion.OutputTokens);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalRun?> GetRunAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectEvalRun);
        DbHelpers.Add(command, "id", evalRunId);
        DbHelpers.Add(command, "tenant_id", tenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<EvalRun?> GetRunByJobIdAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectEvalRunByJobId);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "job_id", jobId);

        return await DbHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
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
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Max(query.Take, 0));

        return await DbHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "Sonuc kosunun ALTINA yazilir ve kiracisini kosudan miras alir; okuma tarafi (ListCaseResultsAsync) kiraciyla sinirlidir.")]
    public async ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var command = CreateCommand(_sql.InsertEvalCaseResult);
        DbHelpers.Add(command, "id", result.Id == Guid.Empty ? AgentPrismId.NewId() : result.Id);
        DbHelpers.Add(command, "eval_run_id", result.EvalRunId);
        DbHelpers.Add(command, "case_id", result.CaseId);
        AddNullableUuid(command, "run_id", result.RunId);
        DbHelpers.Add(command, "passed", result.Passed);
        AddNullableText(command, "output", result.Output);

        // Scores bir LISTEDIR ("[]" sutun varsayilanidir); ayarlanmamis
        // (Undefined) bir JsonElement RawJson ile "null" yazardi ve SQL
        // Server'in ISJSON kisiti bunu REDDEDER (ISJSON(N'null') = 0). Postgre/
        // SQLite'ta json/jsonb 'null' sessizce kabul edildigi icin bu sadece
        // SQL Server'da gorulur.
        Dialect.AddJsonb(command, "scores", result.Scores.ValueKind == JsonValueKind.Undefined ? "[]" : RawJson(result.Scores));

        AddNullableText(command, "failure_reason", result.FailureReason);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(
        string tenantId,
        Guid evalRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var command = CreateCommand(_sql.SelectEvalCaseResults);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "eval_run_id", evalRunId);

        return await DbHelpers.ReadListAsync(command, ReadCaseResult, cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private static EvalSuite ReadSuite(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = DbHelpers.GetNullableString(reader, 3),
            AgentName = reader.GetString(4),
            Checks = ReadJsonb(reader, 5),
            CreatedAt = DbHelpers.GetTimestamp(reader, 6),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 7),
        };

    private static EvalCase ReadCase(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            SuiteId = reader.GetGuid(1),
            Seq = reader.GetInt32(2),
            Query = reader.GetString(3),
            ExpectedOutput = DbHelpers.GetNullableString(reader, 4),
            ExpectedTools = DbHelpers.GetNullableString(reader, 5) is { Length: > 0 } tools
                ? tools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [],
            Context = DbHelpers.GetNullableString(reader, 6),
            SourceRunId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            SourceKind = reader.IsDBNull(8) ? null : (EvalCaseSource)reader.GetInt16(8),
            PromotedAt = DbHelpers.GetNullableTimestamp(reader, 9),
        };

    private static EvalRun ReadRun(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            SuiteId = reader.GetGuid(2),
            JobId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            AgentVersion = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            ModelId = DbHelpers.GetNullableString(reader, 5),
            Status = (EvalRunStatus)reader.GetInt16(6),
            Total = reader.GetInt32(7),
            Passed = reader.GetInt32(8),
            Failed = reader.GetInt32(9),
            InputTokens = reader.IsDBNull(10) ? null : reader.GetInt64(10),
            OutputTokens = reader.IsDBNull(11) ? null : reader.GetInt64(11),
            StartedAt = DbHelpers.GetTimestamp(reader, 12),
            CompletedAt = DbHelpers.GetNullableTimestamp(reader, 13),
        };

    private static EvalCaseResult ReadCaseResult(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            EvalRunId = reader.GetGuid(1),
            CaseId = reader.GetGuid(2),
            RunId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
            Passed = reader.GetBoolean(4),
            Output = DbHelpers.GetNullableString(reader, 5),
            Scores = ReadJsonb(reader, 6),
            FailureReason = DbHelpers.GetNullableString(reader, 7),
        };

    /// <summary>
    /// jsonb sutununu <see cref="JsonElement"/> olarak okur.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonDocument"/> birakildiginda kendi tamponunu geri verir ve
    /// icinden alinan <see cref="JsonElement"/> gecersizlesir; <c>Clone()</c>
    /// tamponu kopyalar ve degeri cagiranin omrunden bagimsiz kilar.
    /// </remarks>
    private static JsonElement ReadJsonb(DbDataReader reader, int ordinal)
    {
        using var document = JsonDocument.Parse(reader.GetString(ordinal));
        return document.RootElement.Clone();
    }

    private static string RawJson(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Undefined ? "null" : payload.GetRawText();

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    private void AddNullableUuid(DbCommand command, string name, Guid? value)
        => Dialect.AddUuid(command, name, value);
}
