using Tracon.Workflows.UnitTests.Fakes;
using static Tracon.Workflows.UnitTests.Fakes.TimeoutRuns;
using static Tracon.Workflows.UnitTests.WorkflowFunctionTimeoutTests;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Two tenants with a workflow of the same name (failure mode 8): each run takes
/// the node timeout of its own tenant's definition.
/// </summary>
/// <remarks>
/// A workflow name is unique only within a tenant. The function registry is shared by
/// every tenant, and the agent wrapper cache was once keyed without the tenant;
/// a timeout kept anywhere outside the definition would leak the same way.
/// </remarks>
public sealed class WorkflowFunctionTimeoutTenantTests
{
    [Fact]
    public async Task Each_tenants_run_uses_the_node_timeout_its_own_definition_sets()
    {
        var clock = new ManualClock();
        var host = Host(clock);
        var started = Signal();

        RegisterHanging(host, "render", started, registered: null);

        host.TenantContext.TenantId = "tenant-a";
        await SaveAsync(host, "chain", Function("render", timeout: TimeSpan.FromSeconds(2)));

        host.TenantContext.TenantId = "tenant-b";
        await SaveAsync(host, "chain", Function("render", timeout: TimeSpan.FromSeconds(7)));

        var runner = host.CreateRunnerWithTimeProvider(clock, static options => options.RunTimeout = TimeSpan.FromMinutes(10));

        // Tenant B runs first, so a value cached by the first run would reach A.
        var forB = Collect(runner, "chain");

        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ArmedAsync(clock, TimeSpan.FromSeconds(7));
        clock.Advance(TimeSpan.FromSeconds(7));

        var runB = await RunOf(host, await forB.WaitAsync(TestContext.Current.CancellationToken));

        runB.TenantId.ShouldBe("tenant-b");
        runB.Error!.Message.ShouldContain("7 s");

        host.TenantContext.TenantId = "tenant-a";

        var forA = Collect(runner, "chain");

        await ArmedAsync(clock, TimeSpan.FromSeconds(2));
        clock.Pending.ShouldNotContain(TimeSpan.FromSeconds(7));
        clock.Advance(TimeSpan.FromSeconds(2));

        var runA = await RunOf(host, await forA.WaitAsync(TestContext.Current.CancellationToken));

        runA.TenantId.ShouldBe("tenant-a");
        runA.Error!.Message.ShouldContain("2 s");
    }
}
