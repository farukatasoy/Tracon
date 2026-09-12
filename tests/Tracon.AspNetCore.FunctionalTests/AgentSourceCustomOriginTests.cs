using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// 101.2: an agent from a custom <see cref="IAgentSource"/> gets
/// <see cref="AgentDefinitionOrigin.Custom"/>. The management API must treat it the
/// same way it treats a code agent — read-only, <c>409</c> on write — instead of the
/// pre-101 behavior, where an unrecognized origin fell through to the "database
/// definition" branch and opened an edit form for an agent nothing could persist.
/// </summary>
public sealed class AgentSourceCustomOriginTests
{
    private const string AgentName = "custom-source-agent";
    private const string SourceName = "custom-test-source";

    [Fact]
    public async Task Detail_reports_Custom_origin_and_is_not_editable()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.GetAsync(new Uri($"/tracon/api/agents/{AgentName}", UriKind.Relative));
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("isEditable").GetBoolean().ShouldBeFalse();
        json.GetProperty("descriptor").GetProperty("origin").GetString().ShouldBe("Custom");
        json.GetProperty("descriptor").GetProperty("sourceName").GetString().ShouldBe(SourceName);
    }

    [Fact]
    public async Task Update_is_rejected_with_409_naming_the_source()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}", UriKind.Relative),
            TestData.Request(AgentName));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("detail").GetString()!.ShouldContain(SourceName);
    }

    [Fact]
    public async Task Delete_is_rejected_with_409()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.DeleteAsync(new Uri($"/tracon/api/agents/{AgentName}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Creating_a_definition_with_the_same_name_is_rejected_and_names_the_source()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request(AgentName));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("detail").GetString()!.ShouldContain(SourceName);
    }

    [Fact]
    public async Task The_agent_appears_in_the_catalog_listing()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddAgentSource(new StubCustomSource()));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative));
        var json = await TraconTestHost.ReadJsonAsync(response);

        json.EnumerateArray()
            .Select(static agent => agent.GetProperty("name").GetString())
            .ShouldContain(static name => name == AgentName);
    }

    /// <summary>A minimal <see cref="IAgentSource"/> that lists one agent but never resolves it — these tests exercise only the management API's read/write guard, not a real run.</summary>
    private sealed class StubCustomSource : IAgentSource
    {
        public string Name => SourceName;

        public int Priority => 500;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor { Name = AgentName, Origin = AgentDefinitionOrigin.Custom, SourceName = Name },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
