using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Workspace;

/// <summary>What the workspace tests share: a run scope, the limits, and a store with the walk fallback.</summary>
// MAAI001: AgentFileStore is marked "evaluation purposes only" - see K-020.
#pragma warning disable MAAI001
internal static class WorkspaceTestSupport
{
    /// <summary>The scope of a run in <paramref name="session"/> of <paramref name="agent"/>.</summary>
    public static AgentRunScope Run(string tenant, string agent, string? session, bool sessionHasNoRecord = false)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = agent,
            TenantId = tenant,
            UserId = "ada",
            SessionId = session,
            SessionHasNoRecord = sessionHasNoRecord,
        };

    /// <summary>Limits that a test can pass in a few bytes. The cost of a row is zero unless a test asks for it.</summary>
    public static IOptionsMonitor<TraconWorkspaceOptions> Limits(long file = 64, long session = 256, long tenant = 1024, long overhead = 0)
        => new StaticOptionsMonitor<TraconWorkspaceOptions>(
            new TraconWorkspaceOptions { MaxFileBytes = file, MaxSessionBytes = session, MaxTenantBytes = tenant, FileOverheadBytes = overhead });

    /// <summary>The services a compiler needs to build a workspace provider: the tool chain and the limits.</summary>
    public static ServiceProvider Services(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BuiltInToolGovernance(
            new AllowAllToolAuthorizationHandler(),
            TestData.OpenToolPolicy(),
            NoOpToolArgumentsValidator.Instance,
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions()),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance));
        services.AddSingleton(Limits());
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>The store of one compiled agent over <paramref name="inner"/>.</summary>
    public static SessionWorkspaceFileStore Store(
        AgentFileStore inner,
        string tenant = "acme",
        string agent = "drafter",
        IOptionsMonitor<TraconWorkspaceOptions>? limits = null)
        => new(inner, new GenericAgentFileTree(inner), tenant, agent, limits ?? Limits());
}
#pragma warning restore MAAI001
