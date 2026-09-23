using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The runs screens: the event trace, the run tree and the runs list.
/// </summary>
public sealed class RunTests(BrowserFixture browsers)
{
    /// <summary>Pattern that matches the "N run(s)" button on the session page.</summary>
    private static readonly Regex SessionRunsButtonPattern =
        new(@"^\d+ runs?$", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Run_events_appear_step_by_step_on_screen()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo: where is ORD-7")).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        // Event names are a stable contract; the screen shows them as-is.
        foreach (var name in new[] { "run.started", "tool.invoking", "tool.invoked", "run.completed" })
        {
            await Expect(session.Page.GetByText(name, new() { Exact = true }).First).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Custom_run_event_renders_as_a_generic_card_named_after_its_CustomType()
    {
        // Phase 141: a consumer-written RunEventType.Custom event reaches the
        // SSE stream and the console draws it with the consumer's OWN
        // CustomType as its name, not a generic "Custom" label -- proving
        // both that the field survives the wire and that 141.2's card renders.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/custom-event-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("prepare ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Preview for order ORD-7 is ready.").First).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        await Expect(session.Page.GetByText("contoso.preview-ready", new() { Exact = true }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Child_agent_run_appears_as_a_tree()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/router");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo:").First).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        // Summary events appear in the root stream; the child run's full
        // stream is not mirrored (event volume would multiply across the tree).
        await Expect(session.Page.GetByText("child.started", new() { Exact = true }).First).ToBeVisibleAsync();

        // The tree panel must show both the root and the child run.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Call tree" })).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("this run", new() { Exact = true }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Child_run_trace_panel_shows_go_to_root_link_and_does_not_hang_loading()
    {
        // BUG-S4-013: once the trace query fell outside `enabled: finished &&
        // parentRunId == null` (true for every child run), React Query's
        // `isPending` stayed PERMANENTLY true; because the panel checked this
        // FIRST, it showed an endless "Loading" and the "root run" link never
        // appeared.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/router");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo:").First).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        var callTree = session.Page.Locator(
            "section", new() { Has = session.Page.GetByRole(AriaRole.Heading, new() { Name = "Call tree" }) });

        await Expect(callTree).ToBeVisibleAsync();

        // The only link in the tree row is the child run (the root row itself
        // - "this run" - is plain text, not a link).
        await callTree.Locator("a[href*='/runs/']").First.ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Trace" })).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("Spans live on the root run")).ToBeVisibleAsync();
        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "root run" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Runs_screen_lists_only_roots_by_default()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/router");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo:").First).ToBeVisibleAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // The default view lists only roots and reports the child run count
        // with a badge.
        await Expect(session.Page.GetByText("1 child run", new() { Exact = true }).First).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("depth 1", new() { Exact = true })).ToHaveCountAsync(0);

        await session.Page.GetByRole(AriaRole.Combobox).Last.SelectOptionAsync("all");

        await Expect(session.Page.GetByText("depth 1", new() { Exact = true }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Runs_button_on_session_page_navigates_to_filtered_list()
    {
        // BUG-S4-017: navigate() carried the query string (?sessionId=...)
        // into path matching; clicking inside the SPA produced "Page not
        // found", only a full page reload (window.location.pathname naturally
        // drops the query) worked.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo:").First).ToBeVisibleAsync();

        await session.Page.Locator("a[href*='/sessions/']").First.ClickAsync();

        // 🚨 A LINK, not a button, since phase 165. The control used to be
        // `<Link><Button>N runs</Button></Link>` — a <button> inside an <a>,
        // which is invalid HTML, two tab stops for one destination, and a click
        // whose behaviour depended on which of the two the pointer hit. It is
        // one anchor now (`LinkButton`), so it navigates, obeys middle-click and
        // copy-link, and reports the role it actually is. The behaviour this
        // case pins — that clicking it reaches the filtered list — is unchanged.
        var runsButton = session.Page.GetByRole(AriaRole.Link, new() { NameRegex = SessionRunsButtonPattern });

        await Expect(runsButton).ToBeVisibleAsync();

        var label = await runsButton.TextContentAsync();
        var expectedCount = int.Parse(label!.Split(' ')[0], CultureInfo.InvariantCulture);

        // Keep the screen's loading state observable. The heading renders before
        // the filtered runs request completes, so an assertion that reads the
        // table immediately after navigation races the network response.
        await session.Page.RouteAsync("**/api/runs**", async route =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750)); // delay: simulated
            await route.ContinueAsync();
        });

        await runsButton.ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Runs" })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Page not found")).ToHaveCountAsync(0);

        await Expect(session.Page.Locator("tbody tr"))
            .ToHaveCountAsync(expectedCount);
    }

    [Fact]
    public async Task Run_identifier_is_monospace_and_carries_a_copy_control()
    {
        // A run id is the thing an operator moves into a query or a ticket.
        // Every identifier in the console is monospace, at one size, and the
        // ones worth copying carry a control instead of asking to be retyped.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await Expect(session.Page.GetByText("Echo:").First).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Transcript", Exact = true })).ToBeVisibleAsync();

        // Scoped to the screen's own header: the top bar carries a monospace
        // ⌘K hint of its own, which is not an identifier.
        var identifier = session.Page.Locator("main header span.font-mono").First;
        await Expect(identifier).ToBeVisibleAsync();

        var family = await identifier.EvaluateAsync<string>("element => getComputedStyle(element).fontFamily");
        family.ShouldContain("mono", Case.Insensitive);

        var shown = (await identifier.InnerTextAsync()).Trim();

        await identifier.Locator("xpath=..").GetByRole(AriaRole.Button, new() { Name = "Copy", Exact = true }).ClickAsync();

        var copied = await session.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        copied.ShouldBe(shown, "The copy control did not put the identifier on the clipboard.");
    }
}
