
namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IRunStore"/> tool-invocation contract.
/// </summary>
/// <remarks>
/// Every implementation must pass the same scenarios; the summary is
/// computed <em>in the store itself</em>, not by fetching rows and
/// aggregating them in memory.
/// </remarks>
public abstract class ToolInvocationContract : TenantIsolationContract<IRunStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// A tool invocation is attached to a run; isolation is established
    /// through the run's tenant.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId), cancellationToken);
        await Store.RecordToolInvocationAsync(Invocation(runId, name, TimeSpan.FromMilliseconds(12)), cancellationToken);

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.ListToolInvocationsAsync((Guid)key)).Count > 0;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.GetToolUsageAsync(new ToolUsageQuery(), cancellationToken)).Count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The run this contract's subject hangs off is opened first, with a live
    /// token: the surface under test is the invocation record, not the run.
    /// </remarks>
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = TenantA;
        await Store.StartRunAsync(TestData.Run(_cancellationRunId), CancellationToken.None);

        await Store.RecordToolInvocationAsync(
            Invocation(_cancellationRunId, "cancelled", TimeSpan.FromMilliseconds(12)),
            cancellationToken);
    }

    // The run the cancellation write records its invocation against.
    private readonly Guid _cancellationRunId = TraconId.NewId();

    [Fact]
    public async Task Invocation_fields_round_trip()
    {
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var invocation = new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = "get_order_status",
            ToolCallId = "call-1",
            Source = "github",
            Arguments = "orderId=ORD-1",
            Result = "shipped",
            Duration = TimeSpan.FromMilliseconds(1234),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Store.RecordToolInvocationAsync(invocation);

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.ToolName.ShouldBe("get_order_status");
        string.Equals(stored.ToolCallId, "call-1", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(stored.Source, "github", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(stored.Arguments, "orderId=ORD-1", StringComparison.Ordinal).ShouldBeTrue();
        stored.Succeeded.ShouldBeTrue();
        stored.Duration.ShouldNotBeNull();
        stored.Duration.Value.TotalMilliseconds.ShouldBe(1234, tolerance: 1);
    }

    [Fact]
    public async Task Non_token_measurement_round_trips()
    {
        // Phase 28: voice tools are billed by CHARACTER or SECOND, not token.
        // Five columns (unit, quantity, estimate, amount, currency) must
        // round-trip identically across all three dialects.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = "speak",
            ToolCallId = "call-voice",
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Characters,
                Quantity = 1234.5m,

                // 🚨 Verifies the fractional part is NOT TRUNCATED. On SQL
                // Server, a decimal parameter with no explicit type is
                // treated as decimal(18,0) and the fraction is silently
                // dropped (a phase 23 lesson).
                Cost = 0.0001357m,
                Currency = "USD",
                IsEstimated = true,
            },
        });

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Usage.ShouldNotBeNull();
        string.Equals(stored.Usage.Unit, ToolUsageUnits.Characters, StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.Quantity.ShouldBe(1234.5m);
        stored.Usage.Cost.ShouldBe(0.0001357m);
        string.Equals(stored.Usage.Currency, "USD", StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.IsEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Invocation_with_no_reported_usage_comes_back_with_EMPTY_usage()
    {
        // The vast majority of invocations carry no usage. Returning an
        // empty ToolCallUsage would mean "measured but zero".
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "get_order_status", TimeSpan.Zero));

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_duration_stays_empty()
    {
        // In a non-streaming run, the call and the result are observed at the
        // same instant; writing a near-zero duration would produce incorrect
        // data.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", duration: null));

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Duration.ShouldBeNull();
    }

    [Fact]
    public async Task Failed_invocation_is_counted_as_unsuccessful()
    {
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(
            Invocation(runId, "tool_a", duration: TimeSpan.FromMilliseconds(5)) with
            {
                Error = "exploded",
            });

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Succeeded.ShouldBeFalse();
        string.Equals(stored.Error, "exploded", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Late_settlement_fills_in_the_timed_out_record_without_rewriting_what_the_model_was_told()
    {
        // A timeout stops the wait, not the work. A body that ignores its
        // cancellation can still succeed and still spend money; that spend has
        // to reach the row every cost query already reads.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = "generate_image",
            ToolCallId = "call-late",
            CreatedAt = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromSeconds(30),
            Error = "Tool 'generate_image' did not complete within 30s.",
            TimedOut = true,
        });

        var settledAt = DateTimeOffset.UtcNow;

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-late",
            LateCompletedAt = settledAt,
            Result = "Images produced. count=1.",
            Duration = TimeSpan.FromSeconds(34),
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Images,
                Quantity = 1m,

                // Same fractional-precision guard as the measurement test
                // above: SQL Server silently truncates an untyped decimal.
                Cost = 0.0401357m,
                Currency = "USD",
            },
        })).ShouldBeTrue();

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        // What the model was told is history and is NOT rewritten.
        stored.TimedOut.ShouldBeTrue();
        stored.Succeeded.ShouldBeFalse();
        string.Equals(stored.Error, "Tool 'generate_image' did not complete within 30s.", StringComparison.Ordinal)
            .ShouldBeTrue();

        // What the timeout left empty is now filled in.
        string.Equals(stored.Result, "Images produced. count=1.", StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.ShouldNotBeNull();
        stored.Usage.Cost.ShouldBe(0.0401357m);
        stored.Usage.Quantity.ShouldBe(1m);
        stored.LateCompletedAt.ShouldNotBeNull();
        stored.LateCompletedAt.Value.ShouldBe(settledAt, tolerance: TimeSpan.FromSeconds(1));
        stored.Duration.ShouldNotBeNull();
        stored.Duration.Value.TotalSeconds.ShouldBe(34, tolerance: 1);
    }

    [Fact]
    public async Task A_late_settlement_never_erases_a_measurement_the_row_already_carries()
    {
        // 🚨 A tool that reports its usage BEFORE it hangs — which is what
        // GenerateImageTool does, reporting before it writes the attachment —
        // already has that measurement on its row: the accumulator was drained
        // when the timeout was recorded, so the late write carries none of its
        // own. Assigning the columns flat would erase the very charge this
        // whole path exists to keep.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = "generate_image",
            ToolCallId = "call-reported-early",
            CreatedAt = DateTimeOffset.UtcNow,
            Error = "Tool 'generate_image' did not complete within 30s.",
            TimedOut = true,
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Images,
                Quantity = 1m,
                Cost = 0.04m,
                Currency = "USD",
            },
        });

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-reported-early",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "Images produced. count=1.",
            Duration = TimeSpan.FromSeconds(34),
            Usage = null,
        })).ShouldBeTrue();

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Usage.ShouldNotBeNull();
        stored.Usage.Cost.ShouldBe(0.04m);
        stored.Usage.Quantity.ShouldBe(1m);
        string.Equals(stored.Result, "Images produced. count=1.", StringComparison.Ordinal).ShouldBeTrue();
        stored.LateCompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_second_late_settlement_for_the_same_call_changes_nothing()
    {
        // The continuation runs once per call, but a retried write must never
        // overwrite a settled outcome with a second one.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(
            Invocation(runId, "slow_tool", TimeSpan.FromSeconds(30)) with
            {
                ToolCallId = "call-once",
                Error = "Tool 'slow_tool' did not complete within 30s.",
                TimedOut = true,
            });

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-once",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "first",
        })).ShouldBeTrue();

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-once",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "second",
        })).ShouldBeFalse();

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        string.Equals(stored.Result, "first", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Late_settlement_touches_only_the_call_it_names()
    {
        // One run may call the same tool several times and only one of them
        // outlived its timeout. Matching on the tool name would book the
        // charge against the wrong call.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(
            Invocation(runId, "slow_tool", TimeSpan.FromMilliseconds(10)) with { ToolCallId = "call-fast" });
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "slow_tool", TimeSpan.FromSeconds(30)) with
            {
                ToolCallId = "call-slow",
                Error = "Tool 'slow_tool' did not complete within 30s.",
                TimedOut = true,
            });

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-slow",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "arrived late",
        })).ShouldBeTrue();

        var stored = await Store.ListToolInvocationsAsync(runId);

        var fast = stored.Single(record => string.Equals(record.ToolCallId, "call-fast", StringComparison.Ordinal));
        var slow = stored.Single(record => string.Equals(record.ToolCallId, "call-slow", StringComparison.Ordinal));

        fast.LateCompletedAt.ShouldBeNull();
        string.Equals(slow.Result, "arrived late", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Late_settlement_for_an_unknown_call_reports_that_it_wrote_nothing()
    {
        // Not an error: the call settles after its run was reported done, and
        // retention may legitimately have removed the run in between.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-that-never-was",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "orphan",
        })).ShouldBeFalse();
    }

    [Fact]
    public async Task Late_settlement_from_the_wrong_tenant_writes_nothing()
    {
        // K-355: an EXPECTED-tenant write guard. The SQL side updates zero
        // rows rather than throwing; the in-memory side must not disagree.
        AmbientTenant.TenantId = TenantA;

        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "slow_tool", TimeSpan.FromSeconds(30)) with
            {
                ToolCallId = "call-tenant",
                TimedOut = true,
            });

        (await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-tenant",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "crossed the boundary",
            TenantId = TenantB,
        })).ShouldBeFalse();

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.LateCompletedAt.ShouldBeNull();
        stored.Result.ShouldBeNull();
    }

    [Fact]
    public async Task A_late_settlement_does_not_count_as_a_second_call()
    {
        // 🚨 The reason a second ROW was rejected: GetToolUsageAsync counts
        // rows. An extra row would report one call as two and halve the
        // tool's error rate.
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(
            Invocation(runId, "slow_tool", TimeSpan.FromSeconds(30)) with
            {
                ToolCallId = "call-counted",
                Error = "Tool 'slow_tool' did not complete within 30s.",
                TimedOut = true,
            });

        await Store.CompleteLateToolInvocationAsync(new LateToolCompletion
        {
            RunId = runId,
            ToolCallId = "call-counted",
            LateCompletedAt = DateTimeOffset.UtcNow,
            Result = "arrived late",
        });

        var usage = (await Store.GetToolUsageAsync(new ToolUsageQuery()))
            .Single(tool => string.Equals(tool.ToolName, "slow_tool", StringComparison.Ordinal));

        usage.TotalCalls.ShouldBe(1);
        usage.FailedCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Invocations_come_back_in_time_order()
    {
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var start = DateTimeOffset.UtcNow;

        // Deliberately written in reverse order: the store must be the one
        // that orders them.
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "second", TimeSpan.Zero) with { CreatedAt = start.AddSeconds(2) });
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "first", TimeSpan.Zero) with { CreatedAt = start });

        var names = (await Store.ListToolInvocationsAsync(runId))
            .Select(static record => record.ToolName)
            .ToList();

        names.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task Summary_is_aggregated_per_tool()
    {
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", TimeSpan.FromMilliseconds(100)));
        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", TimeSpan.FromMilliseconds(300)));
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "tool_a", TimeSpan.FromMilliseconds(200)) with { Error = "error" });
        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_b", TimeSpan.FromMilliseconds(50)));

        var usage = await Store.GetToolUsageAsync(new ToolUsageQuery());

        // Shouldly's ShouldContain(predicate) overload returns void; the
        // matching item is picked with LINQ so it can be used afterward.
        var toolA = usage
            .Single(row => string.Equals(row.ToolName, "tool_a", StringComparison.Ordinal));

        toolA.TotalCalls.ShouldBe(3);
        toolA.FailedCalls.ShouldBe(1);
        toolA.AverageDurationMs.ShouldNotBeNull();
        toolA.AverageDurationMs.Value.ShouldBe(200, tolerance: 1);
        toolA.ErrorRate.ShouldNotBeNull();
        toolA.ErrorRate.Value.ShouldBe(1.0 / 3, tolerance: 0.001);

        usage.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Summary_is_ordered_by_call_count_and_limited()
    {
        var runId = TraconId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "few", TimeSpan.Zero));

        for (var index = 0; index < 3; index++)
        {
            await Store.RecordToolInvocationAsync(Invocation(runId, "many", TimeSpan.Zero));
        }

        var usage = await Store.GetToolUsageAsync(new ToolUsageQuery { MaxTools = 1 });

        usage.Count.ShouldBe(1);
        string.Equals(usage[0].ToolName, "many", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Summary_does_not_count_another_tenants_invocations()
    {
        var mine = TraconId.NewId();
        var theirs = TraconId.NewId();

        await Store.StartRunAsync(TestData.Run(mine) with { TenantId = "tenant-a" });
        await Store.StartRunAsync(TestData.Run(theirs) with { TenantId = "tenant-b" });

        await Store.RecordToolInvocationAsync(Invocation(mine, "shared", TimeSpan.Zero));
        await Store.RecordToolInvocationAsync(Invocation(theirs, "shared", TimeSpan.Zero));

        var usage = await Store.GetToolUsageAsync(new ToolUsageQuery { TenantId = "tenant-a" });

        usage.ShouldHaveSingleItem().TotalCalls.ShouldBe(1);
    }

    private static ToolInvocationRecord Invocation(Guid runId, string toolName, TimeSpan? duration)
        => new()
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = toolName,
            ToolCallId = Guid.NewGuid().ToString("N"),
            Duration = duration,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
