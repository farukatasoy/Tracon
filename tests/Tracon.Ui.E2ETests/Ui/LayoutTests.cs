using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// Rules every screen follows: no horizontal overflow at 375px, named and visible focus, tooltips and links.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class LayoutTests(BrowserFixture browsers)
{
    [Fact]
    public async Task General_screens_at_375px_width_do_not_overflow_horizontally()
    {
        // BUG-S4-008. The third root cause on the Tools screen (the badge row
        // not wrapping when a tool is used by more than one agent) cannot be
        // triggered with this fixed data set (each tool belongs to a single
        // agent); that branch is verified separately against a live server
        // (KAPANIS-PLANI.md §6).
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.SetViewportSizeAsync(375, 812);

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Dashboard");

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Settings");
    }

    [Fact]
    public async Task Every_one_of_the_first_ten_Tab_stops_has_an_accessible_name_and_a_visible_focus_ring()
    {
        // Accessibility rule 1 of the instrument layer, measured rather than
        // asserted in prose: walk the keyboard path an operator actually takes
        // into a screen and check every stop on it.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true }).WaitForAsync();

        // Start from the document, not from wherever the load left the caret.
        await session.Page.EvaluateAsync("() => document.body.focus()");

        var nameless = new List<string>();
        var invisible = new List<string>();
        var first = string.Empty;

        for (var stop = 0; stop < 10; stop++)
        {
            await session.Page.Keyboard.PressAsync("Tab");

            var described = await session.Page.EvaluateAsync<string[]>(FocusProbe);

            var description = described[0];
            var name = described[1];
            var outline = described[2];

            if (stop == 0)
            {
                first = name;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                nameless.Add(description);
            }

            if (!string.Equals(outline, "visible", StringComparison.Ordinal))
            {
                invisible.Add(description);
            }
        }

        // The skip link is deliberately the first stop: eighteen navigation
        // entries stand between the top of the page and the screen's content.
        first.ShouldBe("Skip to content");

        nameless.ShouldBeEmpty("These tab stops have no accessible name.");
        invisible.ShouldBeEmpty("These tab stops draw no focus ring.");
    }

    [Fact]
    public async Task Proof_slice_screens_do_not_overflow_horizontally_at_375px_width()
    {
        // The five screens the instrument layer was proved on. The general
        // check above covers the shell; this one covers the dense screens,
        // which is where a fixed-width table or a long identifier shows up.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.SetViewportSizeAsync(375, 812);

        // A real run, so the list and the detail screen have something dense to
        // lay out: an empty table cannot overflow.
        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/dashboard");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true }).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Dashboard");

        await session.Page.GotoAsync($"{host.UiAddress}/runs");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Runs", Exact = true }).WaitForAsync();

        // The run list labels its links with the identifier itself, not with
        // the playground's "run <id>" wording.
        var runLink = session.Page.Locator("tbody a[href*='/runs/']").First;
        await runLink.WaitForAsync(new() { Timeout = 30_000 });
        await AssertNoHorizontalOverflowAsync(session.Page, "Runs");

        await runLink.ClickAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Transcript", Exact = true }).WaitForAsync(new() { Timeout = 30_000 });
        await AssertNoHorizontalOverflowAsync(session.Page, "Run detail");

        await session.Page.GotoAsync($"{host.UiAddress}/approvals");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Approvals", Exact = true }).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Approvals");

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await session.Page.GetByTestId("agent-name").WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Agent editor");
    }

    /// <summary>
    /// Reports what currently has focus: a description for a failure message,
    /// an approximation of its accessible name, and whether it draws a ring.
    /// </summary>
    /// <remarks>
    /// The name is an approximation on purpose — a browser does not expose the
    /// computed accessible name to script. It covers the four ways this console
    /// names a control (aria-label, an associated label, its own text, its
    /// placeholder), which is exactly what the rule being checked is about.
    /// </remarks>
    private const string FocusProbe = """
        () => {
          const element = document.activeElement;

          if (element === null || element === document.body) {
            return ['<body>', '', 'none'];
          }

          const description =
            element.tagName.toLowerCase() +
            (element.getAttribute('data-testid') === null ? '' : `[${element.getAttribute('data-testid')}]`) +
            ' ' + (element.textContent ?? '').trim().slice(0, 40);

          const labelled = element.getAttribute('aria-labelledby');
          const name =
            (element.getAttribute('aria-label') ?? '').trim() ||
            (labelled === null ? '' : (document.getElementById(labelled)?.textContent ?? '').trim()) ||
            (element.textContent ?? '').trim() ||
            (element.getAttribute('placeholder') ?? '').trim() ||
            (element.labels?.[0]?.textContent ?? '').trim() ||
            (element.getAttribute('title') ?? '').trim();

          // `:focus-visible` is already matching — the element got focus from a
          // real Tab press, which is what makes the ring resolve here.
          const style = getComputedStyle(element);
          const ring =
            style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0 ? 'visible' : 'none';

          return [description, name, ring];
        }
        """;

    [Fact]
    public async Task Remaining_screens_do_not_overflow_horizontally_at_375px_width()
    {
        // The twin of `Proof_slice_screens_...`, over the screens phase 165
        // rebuilt. 🚨 It seeds a real run FIRST, because an empty screen cannot
        // overflow: the 375px case that existed before phase 164 walked empty
        // tables for months and never saw the 87px the dashboard was leaking.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        // 🚨 Every list on the walk has to have ROWS. An empty table cannot
        // overflow, so a walk over a fresh tenant proves nothing — the defect
        // phase 164 found had hidden behind exactly that for months.
        await SeedEveryListAsync(host);

        await session.Page.SetViewportSizeAsync(375, 812);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });
        await AssertNoHorizontalOverflowAsync(session.Page, "Playground");

        // Every screen reachable without creating more data, with the heading
        // it renders once it has loaded. A screen that silently failed to load
        // would pass an overflow check trivially, so each one is waited for by
        // name first.
        var screens = new (string Path, string Heading)[]
        {
            ("sessions", "Sessions"),
            ("agents", "Agents"),
            ("tools", "Tools"),
            ("models", "Models"),
            ("skills", "Skills"),
            ("workflows", "Workflows"),
            ("triggers", "Triggers"),
            ("jobs", "Jobs"),
            ("evals", "Evals"),
            ("experiments", "Experiments"),
            ("mcp", "MCP and approvals"),
            ("audit", "Audit"),
            // Diagnostics has no list of its own: it reports this installation's
            // own state, which the in-memory host always populates.
            ("diagnostics", "Diagnostics"),
            ("workflows/new", "New workflow"),
        };

        foreach (var (path, heading) in screens)
        {
            await session.Page.GotoAsync($"{host.UiAddress}/{path}");
            await session.Page
                .GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true })
                .First.WaitForAsync(new() { Timeout = 30_000 });

            await AssertNoHorizontalOverflowAsync(session.Page, heading);
        }

        // A session's detail screen: dense, and only reachable once a run has
        // created the session above.
        await session.Page.GotoAsync($"{host.UiAddress}/sessions");
        var sessionLink = session.Page.Locator("tbody a[href*='/sessions/']").First;
        await sessionLink.WaitForAsync(new() { Timeout = 30_000 });
        await sessionLink.ClickAsync();
        await session.Page
            .GetByRole(AriaRole.Tab, new() { Name = "Chat history", Exact = true })
            .WaitForAsync(new() { Timeout = 30_000 });
        await AssertNoHorizontalOverflowAsync(session.Page, "Session detail");
    }

    [Fact]
    public async Task A_tooltip_near_the_right_edge_stays_inside_the_viewport()
    {
        // 🚨 The defect this pins, found by the walk above: the bubble is
        // centred on its trigger, so a described badge near the right edge of a
        // 375px screen pushed 224px of bubble past the viewport and scrolled
        // the whole page sideways (104px on the models screen). Phase 165 is
        // what exposed it — it put tooltips on badges and column headings,
        // which is precisely where a trigger sits near the edge.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.SetViewportSizeAsync(375, 812);

        await session.Page.GotoAsync($"{host.UiAddress}/models");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Models", Exact = true }).WaitForAsync();

        // Every described badge on the screen, not just the first: the clamp has
        // to hold wherever the trigger happens to sit.
        var described = session.Page.Locator("span[aria-describedby][tabindex='0']");
        var count = await described.CountAsync();

        count.ShouldBeGreaterThan(0, "No described badge on this screen to hover.");

        for (var index = 0; index < count; index++)
        {
            await described.Nth(index).HoverAsync();
            await session.Page.GetByRole(AriaRole.Tooltip).First.WaitForAsync();
            await AssertNoHorizontalOverflowAsync(session.Page, $"Models (tooltip {index})");
        }
    }

    [Fact]
    public async Task A_link_reads_as_a_link_unless_its_call_site_dresses_it()
    {
        // Phase 165 gave `Link` a default appearance, which changed 48 call
        // sites at once. Both halves matter: the bare link has to carry the
        // accent colour, and a call site that passes its own class has to keep
        // winning — a default that overrode them would repaint muted table
        // links and button-shaped navigations alike.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Undressed: the run identifier in the first column.
        var bare = session.Page.Locator("tbody a[href*='/runs/']").First;
        await bare.WaitForAsync(new() { Timeout = 30_000 });
        var bareClass = await bare.GetAttributeAsync("class");
        bareClass.ShouldNotBeNull();
        bareClass.ShouldContain("text-accent");

        // Dressed by its call site: the agent column is deliberately muted so
        // the identifier stays the row's one emphasis.
        var muted = session.Page.Locator("tbody a[href*='/agents/']").First;
        await muted.WaitForAsync();

        var mutedClass = await muted.GetAttributeAsync("class");
        mutedClass.ShouldNotBeNull();
        mutedClass.ShouldContain("text-muted");
        mutedClass.ShouldNotContain("text-accent", Case.Sensitive);

        // Dressed as a button: one anchor, not a <button> nested inside one.
        await session.Page.GotoAsync($"{host.UiAddress}/agents");
        var asButton = session.Page.GetByRole(AriaRole.Link, new() { Name = "New agent", Exact = true });
        await asButton.WaitForAsync();

        var buttonClass = await asButton.GetAttributeAsync("class");
        buttonClass.ShouldNotBeNull();
        buttonClass.ShouldContain("bg-accent");
        (await asButton.Locator("button").CountAsync()).ShouldBe(
            0,
            "A button-shaped link must be one anchor: a <button> inside an <a> is two tab stops for one destination.");
    }
}
