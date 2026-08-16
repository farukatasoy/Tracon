
namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IRunStore"/> tool-invocation contract.
/// </summary>
/// <remarks>
/// Added in phase 6. The in-memory store and the PostgreSQL store must pass
/// the same scenarios; the summary is computed <em>in the store itself</em>
/// in both implementations.
/// </remarks>
public abstract class ToolInvocationContract : TenantIsolationContract<IRunStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// A tool invocation is attached to a run; isolation is established
    /// through the run's tenant.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));
        await Store.RecordToolInvocationAsync(Invocation(runId, name, TimeSpan.FromMilliseconds(12)));

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.ListToolInvocationsAsync((Guid)key)).Count > 0;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.GetToolUsageAsync(new ToolUsageQuery())).Count;
    }

    [Fact]
    public async Task Invocation_fields_round_trip()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var invocation = new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
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
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
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
        var runId = AgentPrismId.NewId();
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
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", duration: null));

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Duration.ShouldBeNull();
    }

    [Fact]
    public async Task Failed_invocation_is_counted_as_unsuccessful()
    {
        var runId = AgentPrismId.NewId();
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
    public async Task Invocations_come_back_in_time_order()
    {
        var runId = AgentPrismId.NewId();
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
        var runId = AgentPrismId.NewId();
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
        var runId = AgentPrismId.NewId();
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
        var mine = AgentPrismId.NewId();
        var theirs = AgentPrismId.NewId();

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
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = toolName,
            ToolCallId = Guid.NewGuid().ToString("N"),
            Duration = duration,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
