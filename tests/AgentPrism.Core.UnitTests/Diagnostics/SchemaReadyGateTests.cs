namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// <see cref="SchemaReadyGate"/> contract.
/// </summary>
/// <remarks>
/// 🚨 The reason these tests exist is a measured defect (Phase 42, 2026-08-08
/// report section 2.3): <c>MigrationHostedService.StartAsync</c> waits fully
/// for migrations, but <c>BackgroundService.StartAsync</c> returns without
/// waiting for <c>ExecuteAsync</c>. If the registration order is
/// <c>.UseMcp()</c> → <c>.UseSqlite()</c>, the first SQL attempt runs before
/// migration finishes and produces "no such table". Rationale: K-354.
/// </remarks>
public sealed class SchemaReadyGateTests
{
    /// <summary>
    /// In-memory setup: no SQL provider is registered. The gate is open BY
    /// ITSELF; otherwise background services would wait forever.
    /// </summary>
    [Fact]
    public async Task Gate_opens_immediately_when_no_SQL_provider_is_registered()
    {
        var gate = new SchemaReadyGate([]);

        await gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// While an SQL provider is registered, the gate stays CLOSED until
    /// <see cref="SchemaReadyGate.MarkReady"/> is called. This is the exact
    /// behavior that closes the defect.
    /// </summary>
    [Fact]
    public async Task Gate_stays_closed_before_MarkReady_when_an_SQL_provider_is_registered()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);

        var waiter = gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.IsReady.ShouldBeFalse();
        waiter.IsCompleted.ShouldBeFalse("The gate must not open before migration finishes.");

        gate.MarkReady();

        await waiter;

        gate.IsReady.ShouldBeTrue();
    }

    /// <summary>
    /// If migration fails, the gate never opens. The host shuts down anyway;
    /// the waiting service exits via cancellation and does not hang forever.
    /// </summary>
    [Fact]
    public async Task Waiter_exits_when_cancelled_before_the_gate_opens()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("PostgreSQL")]);

        using var cts = new CancellationTokenSource();
        var waiter = gate.WaitAsync(cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiter);
        gate.IsReady.ShouldBeFalse();
    }

    /// <summary>Multiple <c>MarkReady</c> calls are harmless.</summary>
    [Fact]
    public async Task MarkReady_can_be_called_more_than_once()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SQLite")]);

        gate.MarkReady();
        gate.MarkReady();

        await gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.IsReady.ShouldBeTrue();
    }

    /// <summary>A waiter that arrives after the gate opens passes immediately.</summary>
    [Fact]
    public async Task Waiter_arriving_after_the_gate_opens_passes_immediately()
    {
        var gate = new SchemaReadyGate([new SqlPersistenceRegistrationMarker("SqlServer")]);

        gate.MarkReady();

        var waiter = gate.WaitAsync(TestContext.Current.CancellationToken);

        await waiter;
        waiter.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
