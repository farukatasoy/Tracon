using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// The two-node acceptance set: what K-1113 promises for two replicas, proved on a real database
/// server.
/// </summary>
/// <remarks>
/// <para>
/// Two hosts share ONE database and nothing else the application would not share: each has its own DI container, its
/// own cancellation registry, its own job worker, its own MCP tool catalog and its own pending OAuth flows. A
/// scenario passes only if the behaviour crosses the database (or, for the OAuth token, the distributed cache the
/// deployment shares). Six behaviours, one partial file each:
/// </para>
/// <list type="number">
/// <item><description>Cancel of a run that another node executes, direct and queued (<c>.Cancel.cs</c>).</description></item>
/// <item><description><c>RateLimit:Scope = Deployment</c>: the limit holds across nodes, for the endpoint and for the inbound trigger (<c>.RateLimit.cs</c>).</description></item>
/// <item><description>An OAuth redirect that lands on the other node, and a token the other node obtained (<c>.OAuth.cs</c>).</description></item>
/// <item><description>An MCP tool is listed on both nodes while single-executor election is on (<c>.McpDiscovery.cs</c>).</description></item>
/// <item><description>The job lease changes hands when a worker process dies - separate operating-system processes, so it lives in the provider projects (<c>TwoProcessLeaseTakeoverContract</c>).</description></item>
/// <item><description>A tenant's workflow concurrency across both nodes (<c>.WorkflowConcurrency.cs</c>).</description></item>
/// </list>
/// <para>
/// A provider's class derives from this one and says which server it runs on. The scenarios are the same on
/// PostgreSQL and SQL Server because the claim is about behaviour, and the SQL under it (the counter upsert, the lease
/// query, the cancel request) is written once per dialect.
/// </para>
/// <para>
/// 🚨 <strong>This is the proof of a narrow promise, not an SLA.</strong> It says nothing about three nodes, and the
/// intervals are shortened: the shipped values are measured by the manual acceptance cases and by
/// <c>bench/capacity</c>.
/// </para>
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    /// <summary>The header a scenario names its tenant with.</summary>
    private const string TenantHeader = "X-Tracon-Tenant";

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the two hosts of one test connect to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();
}
