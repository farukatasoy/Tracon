using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// Phase 270: who may hand off to whom is built, saved and drawn in the console.
/// </summary>
/// <remarks>
/// The path crosses every layer the component tests stand apart from: the edge table the editor sends, the
/// strict rules of the save (<c>WorkflowDefinitionValidator</c>), the graph the framework compiles from the
/// edges, and the graph view that draws it. The drawing is the point of the last check: the graph is read from the
/// compiled workflow, not from the definition, so a topology the compiler did not apply would show a star here.
/// </remarks>
public sealed class WorkflowHandoffEditorTests(BrowserFixture browsers)
{
    [Fact]
    public async Task A_handoff_workflow_built_in_the_console_saves_its_edges_and_the_graph_draws_them()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };
        var page = session.Page;

        await page.GotoAsync($"{host.UiAddress}/workflows/new");

        await page.GetByTestId("workflow-name").FillAsync("triage-desk");
        await page.GetByTestId("workflow-kind").SelectOptionAsync("Handoff");

        foreach (var agent in new[] { "support", "router", "fulfilment" })
        {
            await page.GetByTestId("workflow-add-agent").SelectOptionAsync(agent);
            await Expect(page.GetByTestId("workflow-participants")).ToContainTextAsync(agent);
        }

        // Empty list: the star, and the screen says so.
        await Expect(page.GetByTestId("workflow-handoffs-star")).ToContainTextAsync("support");

        // support -> router (the row's default), support -> fulfilment, router -> support (the way back).
        for (var row = 0; row < 3; row++)
        {
            await page.GetByTestId("workflow-add-handoff").ClickAsync();
        }

        await page.GetByRole(AriaRole.Combobox, new() { Name = "Handoff 2: to", Exact = true }).SelectOptionAsync("fulfilment");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Handoff 3: from", Exact = true }).SelectOptionAsync("router");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Handoff 3: to", Exact = true }).SelectOptionAsync("support");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Reason of handoff 3", Exact = true }).FillAsync("The routing is done.");
        await page.GetByTestId("workflow-max-handoffs").FillAsync("5");

        await page.GetByTestId("workflow-save").ClickAsync();

        await Expect(page).ToHaveURLAsync($"{host.UiAddress}/workflows/triage-desk");

        var saved = await api.GetFromJsonAsync<JsonElement>("api/workflows/triage-desk");
        var edges = saved.GetProperty("handoffs").EnumerateArray()
            .Select(static edge => $"{edge.GetProperty("from").GetString()}>{edge.GetProperty("to").GetString()}")
            .ToList();

        edges.ShouldBe(["support>router", "support>fulfilment", "router>support"]);
        saved.GetProperty("handoffs")[2].GetProperty("reason").GetString().ShouldBe("The routing is done.");
        saved.GetProperty("maxHandoffs").GetInt32().ShouldBe(5);

        // The graph is the COMPILED one: the way back from the router is an edge of it.
        var graph = await api.GetFromJsonAsync<JsonElement>("api/workflows/triage-desk/graph");
        var agents = graph.GetProperty("nodes").EnumerateArray()
            .Where(static node => node.TryGetProperty("agentName", out var name) && name.ValueKind == JsonValueKind.String)
            .ToDictionary(static node => node.GetProperty("id").GetString()!, static node => node.GetProperty("agentName").GetString()!, StringComparer.Ordinal);
        var compiled = graph.GetProperty("edges").EnumerateArray()
            .Select(static edge => (From: edge.GetProperty("from").GetString()!, To: edge.GetProperty("to").GetString()!))
            .Where(edge => agents.ContainsKey(edge.From) && agents.ContainsKey(edge.To))
            .Select(edge => $"{agents[edge.From]}>{agents[edge.To]}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        compiled.ShouldBe(["support>router", "support>fulfilment", "router>support"], ignoreOrder: true);

        // And the screen draws every edge the graph has (arrowheads are the edges' own marker).
        await Expect(page.GetByTestId("workflow-graph")).ToBeVisibleAsync();

        var drawn = graph.GetProperty("edges").GetArrayLength();

        await Expect(page.Locator("[data-testid=workflow-graph] path[marker-end]")).ToHaveCountAsync(drawn);
    }

    [Fact]
    public async Task The_editor_warns_before_a_save_that_changes_an_edge_of_a_stored_workflow()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };
        var page = session.Page;

        using (var created = await api.PutAsJsonAsync("api/workflows/triage-desk", new
        {
            kind = "Handoff",
            agentNames = new[] { "support", "router" },
            handoffs = new[] { new { from = "support", to = "router" } },
        }))
        {
            created.EnsureSuccessStatusCode();
        }

        await page.GotoAsync($"{host.UiAddress}/workflows/triage-desk/edit");

        await Expect(page.GetByTestId("workflow-handoff")).ToHaveCountAsync(1);
        await Expect(page.GetByTestId("workflow-structure-notice")).ToHaveCountAsync(0);

        await page.GetByTestId("workflow-add-handoff").ClickAsync();
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Handoff 2: from", Exact = true }).SelectOptionAsync("router");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Handoff 2: to", Exact = true }).SelectOptionAsync("support");

        await Expect(page.GetByTestId("workflow-structure-notice")).ToContainTextAsync("who hands off to whom");
    }
}
