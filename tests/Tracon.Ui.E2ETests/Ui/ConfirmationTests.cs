using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The confirmation step in front of an irreversible action.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class ConfirmationTests(BrowserFixture browsers)
{
    /// <summary>Pattern that matches the MCP server removal control and its tooltip.</summary>
    private static readonly Regex McpRemovePattern =
        new("^Removes the registration", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Opens the sessions list with one real session on it and returns the
    /// locator for that row's delete control.
    /// </summary>
    /// <remarks>
    /// A session exists only once an agent has actually run with one, so the
    /// walk starts in the playground. Every case below needs the same two
    /// steps, and a case that seeded its own row differently would be proving
    /// something slightly different from its neighbours.
    /// </remarks>
    private static async Task<ILocator> OpenSessionsWithOneRowAsync(Session session, UiHost host)
    {
        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/sessions");
        await session.Page
            .GetByRole(AriaRole.Heading, new() { Name = "Sessions", Exact = true })
            .WaitForAsync(new() { Timeout = 30_000 });

        var trigger = session.Page.GetByRole(AriaRole.Button, new() { Name = "Delete session" }).First;
        await trigger.WaitForAsync(new() { Timeout = 30_000 });

        return trigger;
    }

    [Fact]
    public async Task Confirm_does_not_default_focus_the_destructive_button()
    {
        // 🚨 The whole reason the console stopped using `window.confirm`: every
        // major browser focuses the ACCEPT button of a native dialog, so the
        // Enter key a hurried operator was already pressing deleted the row.
        // Focus opens on Cancel here, and Enter therefore changes nothing.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var trigger = await OpenSessionsWithOneRowAsync(session, host);

        var requests = 0;
        await session.Page.RouteAsync(
            "**/api/sessions/*",
            async route =>
            {
                if (string.Equals(route.Request.Method, "DELETE", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref requests);
                }

                await route.ContinueAsync();
            });

        await trigger.ClickAsync();
        await session.Page.GetByTestId("confirm-delete-session").WaitForAsync(new() { Timeout = 10_000 });

        (await session.Page.GetByTestId("confirm-cancel").EvaluateAsync<bool>(
            "element => element === document.activeElement"))
            .ShouldBeTrue("The dialog did not open with focus on Cancel.");

        // Enter on the focused control activates Cancel, which closes the
        // dialog. The assertion that matters is the request count.
        await session.Page.Keyboard.PressAsync("Enter");
        await session.Page
            .GetByTestId("confirm-delete-session")
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        Volatile.Read(ref requests).ShouldBe(0, "Enter on a fresh confirmation sent a DELETE.");
    }

    [Fact]
    public async Task Escape_and_cancel_close_the_confirmation_without_any_request()
    {
        // The count is the point. A dialog that closes while its action still
        // fires is worse than no dialog at all: the operator believes they
        // backed out. Both exits are walked in one case because they are the
        // same promise made by two controls.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var trigger = await OpenSessionsWithOneRowAsync(session, host);

        var requests = 0;
        await session.Page.RouteAsync(
            "**/api/sessions/*",
            async route =>
            {
                if (string.Equals(route.Request.Method, "DELETE", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref requests);
                }

                await route.ContinueAsync();
            });

        var dialog = session.Page.GetByTestId("confirm-delete-session");

        await trigger.ClickAsync();
        await dialog.WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        await trigger.ClickAsync();
        await dialog.WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByTestId("confirm-cancel").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        Volatile.Read(ref requests).ShouldBe(0, "Backing out of the confirmation still sent a DELETE.");

        // And the row the dialog was about is still there.
        (await session.Page.GetByRole(AriaRole.Button, new() { Name = "Delete session" }).CountAsync())
            .ShouldBeGreaterThan(0, "The session disappeared even though the delete was cancelled.");
    }

    [Fact]
    public async Task Cancelling_the_confirmation_returns_focus_to_the_trigger()
    {
        // Without this a keyboard user is dropped at the top of the document
        // and has to Tab back through the whole table to reach the row they
        // were already on. `useFocusTrap` restores it; this proves the wrapper
        // did not break the restore by rendering the trigger somewhere else.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var trigger = await OpenSessionsWithOneRowAsync(session, host);

        await trigger.FocusAsync();
        await session.Page.Keyboard.PressAsync("Enter");

        var dialog = session.Page.GetByTestId("confirm-delete-session");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });

        await session.Page.GetByTestId("confirm-cancel").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        (await trigger.EvaluateAsync<bool>("element => element === document.activeElement"))
            .ShouldBeTrue("Focus did not return to the control that opened the confirmation.");
    }

    [Fact]
    public async Task Confirming_twice_in_one_frame_sends_one_request()
    {
        // 🚨 `busy` cannot carry this on its own: it only turns true once the
        // caller's mutation has re-rendered, and two clicks fit inside one
        // frame. On a delete that means two requests; on an approval it means a
        // second verdict on a request that no longer exists.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var trigger = await OpenSessionsWithOneRowAsync(session, host);

        var requests = 0;

        await session.Page.RouteAsync(
            "**/api/sessions/*",
            async route =>
            {
                if (!string.Equals(route.Request.Method, "DELETE", StringComparison.Ordinal))
                {
                    await route.ContinueAsync();

                    return;
                }

                Interlocked.Increment(ref requests);

                // Held open so the second click lands while the first is still
                // in flight — the exact window the guard exists for.
                await Task.Delay(1_500); // delay: simulated
                await route.ContinueAsync();
            });

        await trigger.ClickAsync();
        await session.Page.GetByTestId("confirm-delete-session").WaitForAsync(new() { Timeout = 10_000 });

        var accept = session.Page.GetByTestId("confirm-accept");

        // 🚨 BOTH clicks inside one task, from the page itself. Two separate
        // Playwright clicks would not prove the guard at all: React re-renders
        // between them, `busy` disables the button, and a disabled <button>
        // never dispatches — so the handler would be reached once even with no
        // guard in the component. Dispatching twice before React can re-render
        // is the only way to enter the handler twice, which is exactly the
        // window a real double click opens.
        await accept.EvaluateAsync("element => { element.click(); element.click(); }");

        await session.Page
            .GetByTestId("confirm-delete-session")
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });

        Volatile.Read(ref requests).ShouldBe(1, "A double click on the confirmation sent the action twice.");
    }

    [Fact]
    public async Task A_refused_action_keeps_the_confirmation_open_and_says_why()
    {
        // A dialog that closes on a refusal hands the operator a screen that
        // looks exactly like success. It stays open, and the server's own
        // words are inside it. (That those words stay untranslated is K-232,
        // proven in `A_failed_list_shows_the_servers_own_words...`; this case
        // is about WHERE they appear, so it runs in the default locale the
        // shared helper above navigates by.)
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var trigger = await OpenSessionsWithOneRowAsync(session, host);

        var requests = 0;

        await session.Page.RouteAsync(
            "**/api/sessions/*",
            async route =>
            {
                if (!string.Equals(route.Request.Method, "DELETE", StringComparison.Ordinal))
                {
                    await route.ContinueAsync();

                    return;
                }

                Interlocked.Increment(ref requests);

                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 409,
                    ContentType = "application/problem+json",
                    Body = """{"title":"Session store unavailable","detail":"The session store did not answer."}""",
                });
            });

        await trigger.ClickAsync();

        var dialog = session.Page.GetByTestId("confirm-delete-session");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByTestId("confirm-accept").ClickAsync();

        await dialog.GetByText(ServerFailureText, new() { Exact = true })
            .WaitForAsync(new() { Timeout = 30_000 });

        // 🚨 Still ANSWERABLE, not merely still visible. The double-click guard
        // is about one action in flight, so it has to be released when the
        // action settles — an audit found it was released only when the dialog
        // CLOSED, which meant that after a refusal the confirm button looked
        // enabled and did nothing at all. A second press has to reach the
        // server.
        await session.Page.GetByTestId("confirm-accept").ClickAsync();

        await Assertions.Expect(session.Page.GetByTestId("confirm-accept"))
            .ToBeEnabledAsync(new() { Timeout = 30_000 });

        Volatile.Read(ref requests).ShouldBe(
            2,
            "The confirm button went dead after the server refused the first attempt.");

        // And backing out is still possible.
        await session.Page.GetByTestId("confirm-cancel").ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        // The screen's own note carries the failure once the dialog is gone,
        // and its retry REOPENS the confirmation instead of deleting: an action
        // that earns a second step must not have a one-click door beside it.
        await session.Page.GetByTestId("error-retry").First.ClickAsync();
        await dialog.WaitForAsync(new() { Timeout = 10_000 });

        Volatile.Read(ref requests).ShouldBe(2, "The retry fired the delete instead of re-asking.");
    }

    [Fact]
    public async Task The_whole_confirmation_is_reachable_with_the_keyboard_alone()
    {
        // Opened, read, traversed and answered without a pointer. Tab is
        // trapped, so walking forward long enough has to come back round to
        // Cancel rather than escaping to the page behind.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var trigger = await OpenSessionsWithOneRowAsync(session, host);

        await trigger.FocusAsync();
        await session.Page.Keyboard.PressAsync("Enter");

        var dialog = session.Page.GetByTestId("confirm-delete-session");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });

        // Cancel -> confirm -> close -> back to Cancel: three Tab stops, and
        // the fourth wraps. Anything that left the dialog would land on the
        // table behind it, which `data-testid` would not match.
        var stops = new List<string?>();

        for (var index = 0; index < 4; index++)
        {
            stops.Add(await session.Page.EvaluateAsync<string?>(
                "() => document.activeElement?.getAttribute('data-testid') ?? null"));
            await session.Page.Keyboard.PressAsync("Tab");
        }

        stops[0].ShouldBe("confirm-cancel");
        stops.ShouldContain(stop => string.Equals(stop, "confirm-accept", StringComparison.Ordinal));
        stops[3].ShouldBe("confirm-cancel", "Tab escaped the confirmation instead of cycling inside it.");

        // And the answer can be given from the keyboard.
        await session.Page.GetByTestId("confirm-accept").FocusAsync();
        await session.Page.Keyboard.PressAsync("Enter");

        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        await session.Page.GetByText("No session").First.WaitForAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task A_confirmation_fits_a_narrow_screen_in_the_longer_language()
    {
        // The dialog's body is a whole sentence about what is being destroyed,
        // and the Turkish one is the longer of the two — so the phone width and
        // the longer language are measured TOGETHER rather than one at a time.
        // An overflowing confirmation is not cosmetic: the answer buttons are
        // in the footer, and a footer pushed off the right edge is a decision
        // the operator cannot take back OR walk away from.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        await SeedEveryListAsync(host);

        await session.Page.SetViewportSizeAsync(375, 812);
        await session.Page.GotoAsync($"{host.UiAddress}/evals");

        var trigger = session.Page.GetByRole(AriaRole.Button, new() { Name = "Bu seti sil" }).First;
        await trigger.WaitForAsync(new() { Timeout = 30_000 });
        await trigger.ClickAsync();

        var dialog = session.Page.GetByTestId("confirm-delete-eval");
        await dialog.WaitForAsync(new() { Timeout = 10_000 });

        // Both answers are on screen and pressable, not merely present.
        await session.Page.GetByTestId("confirm-cancel").WaitForAsync();
        await session.Page.GetByTestId("confirm-accept").WaitForAsync();

        await AssertNoHorizontalOverflowAsync(session.Page, "Eval delete confirmation (tr, 375px)");
    }

    [Fact]
    public async Task An_action_that_fails_the_criterion_stays_one_click_and_still_says_what_it_does()
    {
        // 🚨 The other half of §175.3, and the half a gate cannot check.
        // Removing an MCP server is irreversible and NOT destructive: the row
        // holds an endpoint, headers and a configuration key NAME (K-059), all
        // typed back from the same form. So it gets the consequence sentence
        // and keeps its single click. Confirming everything is the same as
        // confirming nothing, and this case is what stops the next phase from
        // quietly adding a seventh dialog.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await SeedEveryListAsync(host);

        await session.Page.GotoAsync($"{host.UiAddress}/mcp");
        await session.Page
            .GetByRole(AriaRole.Heading, new() { Name = "MCP and approvals", Exact = true })
            .WaitForAsync(new() { Timeout = 30_000 });

        var remove = session.Page
            .GetByRole(AriaRole.Button, new() { NameRegex = McpRemovePattern })
            .First;
        await remove.WaitForAsync(new() { Timeout = 30_000 });

        // The consequence is readable before the click, on focus rather than
        // only on hover — the tooltip is the layer this action does get.
        await remove.FocusAsync();
        await session.Page
            .GetByRole(AriaRole.Tooltip)
            .Filter(new() { HasTextRegex = McpRemovePattern })
            .First.WaitForAsync(new() { Timeout = 10_000 });

        await remove.ClickAsync();

        // No second step: the row is gone on the one click.
        (await session.Page.GetByTestId("confirm-accept").CountAsync())
            .ShouldBe(0, "An action that fails the §175.3 criterion opened a confirmation.");

        await session.Page
            .GetByText("knowledge-base", new() { Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
    }
}
