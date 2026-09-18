using System.Net;
using Microsoft.Agents.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent that exists but keeps its definition somewhere other than
/// <see cref="IAgentDefinitionStore"/> must not be reported as an agent that does
/// not exist. Both version-history endpoints used to consult the database store
/// alone, so a code agent — visible at <c>GET /api/agents/{name}</c> at the same
/// moment — was answered with "There is no agent named 'x'".
/// </summary>
/// <remarks>
/// The status stays <c>404</c>: the version-history resource genuinely does not
/// exist. Only the reason changes, and the reason is what the caller acts on.
/// The write endpoints already drew this distinction (<c>409</c> naming the
/// origin); these two reads did not.
/// </remarks>
public sealed class AgentVersionHistoryOriginTests
{
    private const string CustomAgentName = "custom-source-agent";
    private const string CustomSourceName = "custom-test-source";

    [Fact]
    public async Task Versions_of_a_code_agent_says_it_is_defined_in_code()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/kod-agent/versions", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldBe("Agent has no version history");
        var detail = json.GetProperty("detail").GetString()!;
        detail.ShouldContain("'kod-agent' is defined in code");
        detail.ShouldNotContain("There is no agent named");
    }

    [Fact]
    public async Task Version_diff_of_a_code_agent_says_it_is_defined_in_code()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/kod-agent/versions/1/diff/2", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldBe("Agent has no version history");
        json.GetProperty("detail").GetString()!.ShouldContain("'kod-agent' is defined in code");
    }

    [Fact]
    public async Task Versions_of_a_custom_source_agent_names_the_source()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/agents/{CustomAgentName}/versions", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldBe("Agent has no version history");
        json.GetProperty("detail").GetString()!.ShouldContain(CustomSourceName);
    }

    [Fact]
    public async Task Version_diff_of_a_custom_source_agent_names_the_source()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/agents/{CustomAgentName}/versions/1/diff/2", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("detail").GetString()!.ShouldContain(CustomSourceName);
    }

    /// <summary>
    /// The opposite direction: a name nothing knows must still get the plain
    /// "no such agent" answer. Widening the message must not swallow it.
    /// </summary>
    [Fact]
    public async Task Versions_of_an_unknown_name_still_says_there_is_no_such_agent()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/no-such-agent/versions", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldBe("Agent not found");
        json.GetProperty("detail").GetString()!.ShouldContain("There is no agent named 'no-such-agent'.");
    }

    [Fact]
    public async Task Version_diff_of_an_unknown_name_still_says_there_is_no_such_agent()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/no-such-agent/versions/1/diff/2", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("title").GetString().ShouldBe("Agent not found");
    }

    /// <summary>Copied from <see cref="AgentSourceCustomOriginTests"/>; lists one agent and never resolves it.</summary>
    private sealed class StubCustomSource : IAgentSource
    {
        public string Name => CustomSourceName;

        public int Priority => 500;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor { Name = CustomAgentName, Origin = AgentDefinitionOrigin.Custom, SourceName = Name },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
