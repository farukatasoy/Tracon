using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The workflows screens: the graph and human-in-the-loop requests.
/// </summary>
public sealed class WorkflowTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Workflow_graph_renders()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows");

        await Expect(session.Page.GetByText("summarize-and-translate")).ToBeVisibleAsync();
        await session.Page.GetByText("summarize-and-translate").ClickAsync();

        var graph = session.Page.GetByTestId("workflow-graph");

        await Expect(graph).ToBeVisibleAsync();

        // The built-in pattern adds an output node that the user did not
        // write: two agents + OutputMessages. The graph is rendered from the
        // COMPILED workflow, not the definition, so that node also appears.
        var nodes = session.Page.GetByTestId("workflow-node");

        // A retrying check, not a one-shot count: the nodes render after the
        // graph container does (Phase 184).
        await Expect(nodes.Nth(2), "The compiled graph shows fewer than three nodes.").ToBeVisibleAsync();
    }

    [Fact]
    public async Task Mermaid_text_can_be_copied()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await session.Page.GotoAsync($"{host.UiAddress}/workflows/summarize-and-translate");

        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).First.ClickAsync();

        var copied = await session.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The copied text is produced by Microsoft Agent Framework; the UI
        // does not render it, it only passes it through (bundle budget, K-002).
        copied.ShouldContain("flowchart", Case.Sensitive);
    }

    /// <summary>
    /// The console cannot change a function-node chain yet, so it must carry it.
    /// </summary>
    /// <remarks>
    /// The editor used to open this workflow with an EMPTY participant list and a
    /// disabled Save. Adding an agent enabled Save, and the full-replace PUT
    /// (K-725) replaced the chain with that agent: the function node was gone,
    /// with no warning. Measured on the sample app on 2026-10-10.
    /// </remarks>
    [Fact]
    public async Task Editing_a_function_node_workflow_keeps_its_chain()
    {
        await using var host = await UiHost.StartAsync(configureTracon: static tracon => tracon
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "word-count",
                static _ => static (messages, _, _) => ValueTask.FromResult(messages),
                "Passes the messages on unchanged."));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        using (var created = await api.PutAsJsonAsync("api/workflows/mixed-chain", new
        {
            description = "Answers, then counts words.",
            kind = "Sequential",
            nodes = new[]
            {
                new { name = "support", kind = "Agent" },
                new { name = "word-count", kind = "Function" },
            },
        }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/mixed-chain/edit");

        var chain = session.Page.GetByTestId("workflow-nodes");

        await Expect(chain).ToContainTextAsync("support");
        await Expect(chain).ToContainTextAsync("word-count");

        await session.Page.GetByLabel("Description", new() { Exact = true }).FillAsync("Edited in the console.");
        await session.Page.GetByTestId("workflow-save").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync($"{host.UiAddress}/workflows/mixed-chain");

        var saved = await api.GetFromJsonAsync<JsonElement>("api/workflows/mixed-chain");

        saved.GetProperty("description").GetString().ShouldBe("Edited in the console.");
        saved.GetProperty("agentNames").GetArrayLength().ShouldBe(0);

        var nodes = saved.GetProperty("nodes");

        nodes.GetArrayLength().ShouldBe(2);
        nodes[0].GetProperty("name").GetString().ShouldBe("support");
        nodes[1].GetProperty("name").GetString().ShouldBe("word-count");
        nodes[1].GetProperty("kind").GetString().ShouldBe("Function");
    }

    [Fact]
    public async Task Pending_request_card_can_be_answered()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");

        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        // The run pauses waiting for a human response; the card appears then.
        var card = session.Page.GetByTestId("workflow-pending-request");

        await Expect(card).ToBeVisibleAsync();

        await Expect(card).ToContainTextAsync("publish the report", new() { UseInnerText = true });

        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        // The response opens a NEW run and produces the graph output.
        var output = session.Page.GetByTestId("workflow-output");

        await Expect(output).ToBeVisibleAsync();

        await Expect(output).ToContainTextAsync("approved", new() { UseInnerText = true });
    }

    [Fact]
    public async Task Pending_run_appears_with_distinct_status_in_Runs_list()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");
        await Expect(session.Page.GetByTestId("workflow-graph")).ToBeVisibleAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();
        await Expect(session.Page.GetByTestId("workflow-pending-request")).ToBeVisibleAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Neither completed nor failed: it has its own status.
        //
        // 🚨 Exact is required: the status FILTER also has a hidden
        // <option>Awaiting input</option>, and a substring match would find
        // that first and wait for it to be visible. The badge is lowercase,
        // the option is capitalized; Exact tells them apart.
        await Expect(session.Page
            .GetByText("awaiting input", new() { Exact = true })).ToBeVisibleAsync();
    }
}
