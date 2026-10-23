using System.Net;
using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// A thread is a session, and a session belongs to the user who opened it and to the tenant it was opened in (Phase 236).
/// </summary>
/// <remarks>
/// 🚨 The identity is switched PER REQUEST, not per host: a test that starts one host per user could never catch the defect
/// that matters most, one user's thread or approval reachable from another user's request, because the two would never
/// share a store.
/// </remarks>
public sealed class AGUISessionOwnershipTests
{
    private const string TenantHeader = "X-Tracon-Tenant";

    private static async Task<(AGUITestHost Host, SwitchableAttribution Identity, StrongBox<int> Executions)> StartAsync(
        bool enabled = true,
        bool tenancy = false)
    {
        var identity = new SwitchableAttribution();
        var executions = new StrongBox<int>();

        var host = await AGUITestHost.StartAsync(
            builder =>
            {
                TestAgents.AddApprovalTool(builder, executions);

                if (tenancy)
                {
                    // The header names the tenant. Without tenancy every request is the one default tenant, and a test that
                    // sends two tenant names would prove nothing.
                    builder.UseTenancy(options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }
            },
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(options =>
                {
                    options.Enabled = enabled;
                    options.RequireAuthenticatedOwner = true;
                });
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
            });

        return (host, identity, executions);
    }

    [Fact]
    public async Task Another_users_thread_is_a_403_and_the_owner_keeps_it()
    {
        var (host, identity, _) = await StartAsync();
        await using var _ = host;

        identity.UserId = "a";
        (await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-owned", "cancel the order", "r1"))).Status.ShouldBe(HttpStatusCode.OK);

        identity.UserId = "b";
        var intruder = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-owned", "what did a ask?", "r2"));

        intruder.Status.ShouldBe(HttpStatusCode.Forbidden, intruder.Body);

        var record = await host.Services.GetRequiredService<ISessionStore>().GetAsync("t-owned", TestContext.Current.CancellationToken);
        record!.OwnerId.ShouldBe("a");

        // Nothing of B's request reached the run store: A's single run is all there is.
        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { SessionId = "t-owned" }, TestContext.Current.CancellationToken);
        runs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Another_users_approval_cannot_be_decided_through_the_thread()
    {
        var (host, identity, executions) = await StartAsync();
        await using var _ = host;

        identity.UserId = "a";
        var first = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-approval", "cancel the order", "r1"));
        var interruptId = first.Interrupts.ShouldHaveSingleItem().Id!;

        identity.UserId = "b";
        var stolen = await host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.With("t-approval", "r2", [], resume: [AGUIInputs.Resolved(interruptId, new { approved = true })]));

        stolen.Status.ShouldBe(HttpStatusCode.Forbidden, stolen.Body);
        executions.Value.ShouldBe(0, "B approved nothing");

        var audit = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Action = "approval.decision" }, TestContext.Current.CancellationToken);
        audit.ShouldBeEmpty("a refused decision leaves no audit row of an applied one");

        // The owner still can.
        identity.UserId = "a";
        var approved = await host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.With("t-approval", "r3", [], resume: [AGUIInputs.Resolved(interruptId, new { approved = true })]));

        approved.Status.ShouldBe(HttpStatusCode.OK, approved.Body);
        executions.Value.ShouldBe(1);
    }

    [Fact]
    public async Task A_thread_of_another_tenant_is_not_reachable_and_its_approval_is_not_decidable()
    {
        var (host, _, executions) = await StartAsync(enabled: false, tenancy: true);
        await using var _ = host;

        var first = await host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.User("t-shared-id", "cancel the order", "r1"),
            request => request.Headers.Add(TenantHeader, "tenant-a"));
        var interruptId = first.Interrupts.ShouldHaveSingleItem().Id!;

        // Tenant B names the same thread identity and tenant A's interrupt: B's thread is B's own, and it holds no such request.
        var other = await host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.With("t-shared-id", "r2", [], resume: [AGUIInputs.Resolved(interruptId, new { approved = true })]),
            request => request.Headers.Add(TenantHeader, "tenant-b"));

        other.Status.ShouldBe(HttpStatusCode.BadRequest, other.Body);
        executions.Value.ShouldBe(0);

        // And tenant A's thread was not touched.
        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { SessionId = "t-shared-id", TenantId = "tenant-a" }, TestContext.Current.CancellationToken);
        runs.ShouldHaveSingleItem().Status.ShouldBe(RunStatus.AwaitingApproval);
    }

    [Fact]
    public async Task A_new_thread_without_an_identity_to_own_it_is_a_403_when_ownership_is_on()
    {
        var (host, identity, _) = await StartAsync();
        await using var _ = host;

        identity.UserId = null;
        var response = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-nobody", "cancel the order", "r1"));

        response.Status.ShouldBe(HttpStatusCode.Forbidden, response.Body);
        response.Problem.GetProperty("errorType").GetString().ShouldBe(TraconSessionOwnerRequiredException.SessionOwnerRequiredErrorType);
    }

    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }
}
