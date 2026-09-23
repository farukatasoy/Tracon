using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The workflows screens: the graph and human-in-the-loop requests.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class WorkflowTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Workflow_graph_renders()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows");

        await session.Page.GetByText("summarize-and-translate").WaitForAsync();
        await session.Page.GetByText("summarize-and-translate").ClickAsync();

        var graph = session.Page.GetByTestId("workflow-graph");

        await graph.WaitForAsync();

        // The built-in pattern adds an output node that the user did not
        // write: two agents + OutputMessages. The graph is rendered from the
        // COMPILED workflow, not the definition, so that node also appears.
        var nodes = session.Page.GetByTestId("workflow-node");

        (await nodes.CountAsync()).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Mermaid_text_can_be_copied()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await session.Page.GotoAsync($"{host.UiAddress}/workflows/summarize-and-translate");

        await session.Page.GetByTestId("workflow-graph").WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).First.ClickAsync();

        var copied = await session.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The copied text is produced by Microsoft Agent Framework; the UI
        // does not render it, it only passes it through (bundle budget, K-002).
        copied.ShouldContain("flowchart", Case.Sensitive);
    }

    [Fact]
    public async Task Pending_request_card_can_be_answered()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");

        await session.Page.GetByTestId("workflow-graph").WaitForAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        // The run pauses waiting for a human response; the card appears then.
        var card = session.Page.GetByTestId("workflow-pending-request");

        await card.WaitForAsync(new() { Timeout = 20_000 });

        (await card.InnerTextAsync()).ShouldContain("publish the report", Case.Sensitive);

        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        // The response opens a NEW run and produces the graph output.
        var output = session.Page.GetByTestId("workflow-output");

        await output.WaitForAsync(new() { Timeout = 20_000 });

        (await output.InnerTextAsync()).ShouldContain("approved", Case.Sensitive);
    }

    [Fact]
    public async Task Pending_run_appears_with_distinct_status_in_Runs_list()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");
        await session.Page.GetByTestId("workflow-graph").WaitForAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();
        await session.Page.GetByTestId("workflow-pending-request").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Neither completed nor failed: it has its own status.
        //
        // 🚨 Exact is required: the status FILTER also has a hidden
        // <option>Awaiting input</option>, and a substring match would find
        // that first and wait for it to be visible. The badge is lowercase,
        // the option is capitalized; Exact tells them apart.
        await session.Page
            .GetByText("awaiting input", new() { Exact = true })
            .WaitForAsync(new() { Timeout = 20_000 });
    }
}
