using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A name that a model or a provider chose, longer than the column that holds it, is shortened and written, and the width the
/// store shortens to is the width of the columns.
/// </summary>
/// <remarks>
/// The name of a tool a model called, the identifier the provider gave the call and the name of a span are not chosen by a
/// client of the API or checked where a tool is registered: a model can call a tool nobody registered by a name of any length.
/// SQL Server refuses a value over <c>nvarchar(200)</c>, and the write that recorded what the run did was lost with it. The
/// limit lives in the dialect and the width in a migration; this reads the migrated schema, so a migration that changes one
/// without the other fails here and not in a production write that is lost.
/// </remarks>
public sealed class ForeignNameWidthTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private static readonly string LongName = "tool:" + new string('t', 400);

    [Fact]
    public async Task The_dialects_limit_is_the_width_of_every_column_that_holds_a_name_a_model_chose()
    {
        var context = await schema.ResetAsync(tenantId: "default");

        var widths = await context.CatalogScalarAsync<string>(
            "SELECT STRING_AGG(CAST(t.name + N'.' + c.name + N'=' + CAST(c.max_length / 2 AS nvarchar(10)) AS nvarchar(max)), N';') " +
            "FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id " +
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND (" +
            "(t.name IN (N'run_events', N'tool_invocations') AND c.name IN (N'tool_name', N'tool_call_id')) " +
            "OR (t.name = N'pending_approvals' AND c.name = N'tool_name') " +
            "OR (t.name = N'spans' AND c.name = N'name'));");

        var limit = new SqlServerDialect("dbo").ForeignNameLimit;

        (widths ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ShouldBe(
            [
                $"pending_approvals.tool_name={limit}",
                $"run_events.tool_call_id={limit}",
                $"run_events.tool_name={limit}",
                $"spans.name={limit}",
                $"tool_invocations.tool_call_id={limit}",
                $"tool_invocations.tool_name={limit}",
            ]);
    }

    [Fact]
    public async Task An_event_a_group_of_events_and_a_tool_call_with_a_name_over_the_column_are_written_shortened()
    {
        var context = await schema.ResetAsync(tenantId: "default");
        var limit = new SqlServerDialect("dbo").ForeignNameLimit;
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });

        await context.Runs.AppendEventAsync(Event(runId, 0));
        await context.Runs.AppendEventsAsync([Event(runId, 1), Event(runId, 2)]);

        await context.Runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ToolName = LongName,
            ToolCallId = LongName,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var events = new List<RunEvent>();

        await foreach (var runEvent in context.Runs.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        events.Count.ShouldBe(3);
        events.ShouldAllBe(runEvent => runEvent.ToolName!.Length == limit && runEvent.ToolCallId!.Length == limit);

        // One shortening, written and looked up: the three events and the call name the same tool with the same text.
        events.Select(static runEvent => runEvent.ToolName).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);

        var invocation = (await context.Runs.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        invocation.ToolName.ShouldBe(events[0].ToolName);
        invocation.ToolCallId.ShouldBe(events[0].ToolCallId);
        invocation.ToolName.Length.ShouldBe(limit);
    }

    [Fact]
    public async Task A_late_completion_finds_the_call_by_the_identifier_it_was_written_with()
    {
        var context = await schema.ResetAsync(tenantId: "default");
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });
        await context.Runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ToolName = "slow_tool",
            ToolCallId = LongName,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var settled = await context.Runs.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = LongName,
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "done",
        });

        settled.ShouldBeTrue("the row was written under the shortened identifier and looked up under the same");
    }

    [Fact]
    public async Task A_span_and_an_approval_with_a_name_over_the_column_are_written_shortened()
    {
        var context = await schema.ResetAsync(tenantId: "default");
        var limit = new SqlServerDialect("dbo").ForeignNameLimit;
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(new RunStartInfo { RunId = runId, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow });

        var spanId = Guid.NewGuid();

        await context.Traces.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = Guid.NewGuid().ToString("N"),
            TenantId = "default",
            RunId = runId,
            Spans =
            [
                new TraceSpan
                {
                    Id = spanId,
                    SpanId = "0123456789abcdef",
                    Name = "execute_tool " + new string('x', 400),
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        (await context.Traces.GetTraceByRunAsync(runId)).ShouldNotBeNull().Spans.ShouldHaveSingleItem().Name.Length.ShouldBe(limit);

        await context.PendingApprovals.CreateAsync(new PendingApproval
        {
            Id = Guid.NewGuid(),
            TenantId = "default",
            RunId = runId,
            SessionId = "session",
            RequestId = "request-1",
            ToolName = LongName,
            Status = ApprovalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        });

        (await context.PendingApprovals.ListPendingAsync()).ShouldHaveSingleItem().ToolName.Length.ShouldBe(limit);
    }

    private static RunEvent Event(Guid runId, long sequence)
        => new()
        {
            RunId = runId,
            Sequence = sequence,
            Type = RunEventType.ToolInvoking,
            Timestamp = DateTimeOffset.UtcNow,
            ToolName = LongName,
            ToolCallId = LongName,
        };
}
