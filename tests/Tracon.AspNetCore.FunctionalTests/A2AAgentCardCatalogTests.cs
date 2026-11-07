using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The A2A agent card says what the agent's catalog entry says at the time the card is asked for.
/// </summary>
/// <remarks>
/// <para>
/// The card takes its description and version from the catalog. The card used to read the catalog once, while the endpoints
/// were being mapped, and that is before the migrations have run on every host that stores agents in SQL: the read found no
/// table, fell back to the agent name and the version <c>1</c>, and kept that card for the life of the process. A stored
/// agent's description and every edit of it never reached the card, and the OpenAPI text of the endpoint promised that
/// editing the description changes the card.
/// </para>
/// <para>
/// It was not only a SQL problem. The read was guarded by <c>SchemaReadyGate.IsReady</c>, and a host without SQL gets a gate
/// that opens only when something first waits on it (<c>WaitAsync</c>), so at map time the guard was closed there too: the
/// card of a code agent carried its name for a description as well. The first test runs on SQLite, where a schema has to be
/// waited for; the second runs without SQL.
/// </para>
/// </remarks>
public sealed class A2AAgentCardCatalogTests
{
    private const string Agent = "db-agent";

    private static readonly Uri AgentsUri = new("/tracon/api/agents", UriKind.Relative);

    private static readonly Uri CardUri = new($"/tracon/a2a/{Agent}/.well-known/agent-card.json", UriKind.Relative);

    [Fact]
    public async Task The_card_of_a_stored_agent_carries_its_description_and_version_and_follows_an_edit()
    {
        using var database = new TempSqliteDatabase("a2a-card-catalog");

        await using var host = await StartAsync(database);

        using var created = await host.Client.PostAsJsonAsync(AgentsUri, Request("Answers questions about orders."), TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var first = await CardAsync(host);

        first.Description.ShouldBe("Answers questions about orders.");
        first.Version.ShouldBe("1");

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri($"{AgentsUri}/{Agent}", UriKind.Relative),
            Request("Answers questions about orders and returns."),
            TestContext.Current.CancellationToken);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await CardAsync(host);

        second.Description.ShouldBe("Answers questions about orders and returns.", "the card is built when it is asked for, not when it was mapped");
        second.Version.ShouldBe("2");
    }

    [Fact]
    public async Task The_card_of_a_code_agent_carries_its_description()
    {
        // A host without SQL: the map-time guard was closed there too (the gate opens when something first waits on it).
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(static options => options.ExposedAgents.Add("kod-agent")),
            configureAfterMap: static app => app.MapTraconA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative),
            TestContext.Current.CancellationToken);
        var body = await TraconTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("description").GetString().ShouldBe("The code agent used in tests.");
        body.GetProperty("version").GetString().ShouldBe("1");
    }

    [Fact]
    public async Task The_card_of_an_agent_the_catalog_does_not_hold_falls_back_to_its_name()
    {
        // The allow list is fixed at registration; an agent in it may not exist (yet). The card still answers.
        using var database = new TempSqliteDatabase("a2a-card-missing");

        await using var host = await StartAsync(database);

        var card = await CardAsync(host);

        card.Description.ShouldBe(Agent);
        card.Version.ShouldBe("1");
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .UseA2A(static options => options.ExposedAgents.Add(Agent)),
            configureAfterMap: static app => app.MapTraconA2A());

    private static AgentDefinitionRequest Request(string description)
        => TestData.Request(Agent) with { Description = description };

    private static async Task<(string? Description, string? Version)> CardAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(CardUri, TestContext.Current.CancellationToken);
        var body = await TraconTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (body.GetProperty("description").GetString(), body.GetProperty("version").GetString());
    }
}
