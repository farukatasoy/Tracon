using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Agents.AI;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// 101.4 at the host boundary: <c>AgentSourceValidationService</c> is an
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/>, so its structural check runs
/// while the real ASP.NET Core host starts — not deferred to the first HTTP request, the
/// way the equivalent <c>CodeAgentSource</c> duplicate-name check used to be before 101.
/// </summary>
public sealed class AgentSourceValidationHttpTests
{
    [Fact]
    public async Task Two_sources_sharing_a_name_stop_the_host_from_starting()
    {
        await Should.ThrowAsync<TraconAgentSourceException>(async () =>
            await TraconTestHost.StartAsync(configureTracon: builder => builder
                .AddAgentSource(new StubSource("duplicate"))
                .AddAgentSource(new StubSource("duplicate"))));
    }

    private sealed class StubSource(string name) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority => 500;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
