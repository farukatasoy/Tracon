using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Agents.AI;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// 101.3 at the HTTP boundary: a broken <see cref="IAgentSource"/> must not take down
/// <c>GET /api/agents</c> for everyone else, and a run against the broken source's own
/// agent must not leak the raw third-party exception text into the response body.
/// </summary>
public sealed class AgentSourceFaultIsolationHttpTests
{
    private const string BrokenSourceName = "broken-test-source";
    private const string BrokenAgentName = "broken-agent";

    [Fact]
    public async Task Listing_stays_available_when_one_source_throws()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition("healthy-agent"))
                .AddAgentSource(new ThrowingSource()));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.EnumerateArray()
            .Select(static agent => agent.GetProperty("name").GetString())
            .ShouldContain(static name => name == "healthy-agent");
    }

    [Fact]
    public async Task Running_the_broken_sources_agent_does_not_leak_the_raw_exception_text()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.AddAgentSource(new ThrowingSource()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/agents/{BrokenAgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        var detail = json.GetProperty("detail").GetString();

        detail.ShouldNotBeNull();
        detail.ShouldNotContain(ThrowingSource.SecretMessage);
        detail.ShouldContain(BrokenSourceName);
        detail.ShouldContain(AgentPrismAgentSourceException.SourceFailedErrorType);
    }

    /// <summary>An <see cref="IAgentSource"/> whose <c>ListAsync</c> and <c>ResolveAsync</c> always throw.</summary>
    private sealed class ThrowingSource : IAgentSource
    {
        public const string SecretMessage = "raw upstream failure with connection string secret";

        public string Name => BrokenSourceName;

        public int Priority => 500;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretMessage);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretMessage);
    }
}
