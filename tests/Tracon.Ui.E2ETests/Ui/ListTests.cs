using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// Rules every list screen follows: failures, filters and expanding rows.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class ListTests(BrowserFixture browsers)
{
    [Fact]
    public async Task A_failed_list_shows_the_servers_own_words_and_retries_on_one_click()
    {
        // Two rules in one walk. The message is the SERVER's, untranslated
        // (K-232) — inventing a sentence for a failure the console does not
        // recognise hides what actually broke. And the note carries a retry, so
        // a 500 is not a dead end that only a page reload escapes.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        // 🚨 Fails while the flag is set, not "the first attempt only": the
        // console retries a 5xx twice on its own (`main.tsx`), so failing one
        // attempt means the user never sees an error at all.
        var failing = true;

        await session.Page.RouteAsync(
            // `*` stops at a path separator, so this matches the LIST
            // (`/api/sessions` with or without a query) and not one session's
            // own address.
            "**/api/sessions*",
            async route =>
            {
                if (Volatile.Read(ref failing))
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 500,
                        ContentType = "application/problem+json",
                        Body = """{"title":"Session store unavailable","detail":"The session store did not answer."}""",
                    });

                    return;
                }

                await route.ContinueAsync();
            });

        await session.Page.GotoAsync($"{host.UiAddress}/sessions");

        // Turkish UI, English server text: the locale is tr-TR above, and the
        // sentence below is still the server's own.
        // The whole sentence: `ErrorNote` prints `error.message`, and
        // `TraconError` builds it as "<title>: <detail>" from the server's
        // problem+json. Matching only the title would assert less than this
        // case claims — that the server's own words reach the screen.
        await session.Page
            .GetByText(ServerFailureText, new() { Exact = true })
            .WaitForAsync(new() { Timeout = 30_000 });

        // Let the store answer, then press the note's own retry.
        Volatile.Write(ref failing, false);

        await session.Page.GetByTestId("error-retry").ClickAsync();

        // The second attempt reaches the real endpoint, so the error clears.
        await session.Page
            .GetByText(ServerFailureText, new() { Exact = true })
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
    }

    [Fact]
    public async Task Filters_are_labelled_and_the_reset_appears_only_once_something_is_filtered()
    {
        // The toolbar contract, on a screen that did not have one before phase
        // 165. A bare <select> in a filter strip has no accessible name — its
        // options are its only text, and "All agents" is not what the control
        // IS — and a reset that is always visible cannot say whether anything
        // is narrowed.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/skills");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Skills", Exact = true }).WaitForAsync();

        (await session.Page.GetByTestId("toolbar-reset").CountAsync()).ShouldBe(
            0,
            "Nothing is filtered yet, so there is nothing to clear.");

        // Named by its visible label, not by whatever its first option says.
        await session.Page
            .GetByRole(AriaRole.Combobox, new() { Name = "Status", Exact = true })
            .SelectOptionAsync("disabled");

        await session.Page.GetByTestId("toolbar-reset").ClickAsync();

        (await session.Page.GetByTestId("toolbar-reset").CountAsync()).ShouldBe(
            0,
            "The reset cleared the filters but stayed on screen.");
    }

    [Fact]
    public async Task An_expanding_row_announces_its_state_and_opens_from_the_keyboard()
    {
        // The audit row used to be an `onClick` on the <tr>: unreachable by
        // keyboard, and announcing nothing about being expandable. The
        // disclosure is a real button now, and it names what it opens.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        // An entry is written as a side effect of storing an agent, which is
        // the only way this screen has anything to expand.
        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);
        await session.Page.GetByTestId("agent-name").FillAsync("audited-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        await session.Page.GetByTestId("agent-save").ClickAsync();
        await session.Page
            .GetByRole(AriaRole.Heading, new() { Name = "audited-agent", Exact = true })
            .WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/audit");

        var disclosure = session.Page.GetByRole(AriaRole.Button, new() { Name = "Details", Exact = true }).First;
        await disclosure.WaitForAsync(new() { Timeout = 30_000 });

        (await disclosure.GetAttributeAsync("aria-expanded")).ShouldBe("false");

        var controls = await disclosure.GetAttributeAsync("aria-controls");
        controls.ShouldNotBeNullOrEmpty("The disclosure does not name the region it opens.");

        await disclosure.FocusAsync();
        await session.Page.Keyboard.PressAsync("Enter");

        await session.Page
            .GetByRole(AriaRole.Button, new() { Name = "Hide", Exact = true })
            .First.WaitForAsync(new() { Timeout = 30_000 });

        (await session.Page.Locator($"#{controls}").CountAsync()).ShouldBe(
            1,
            "aria-controls names an element that is not on the page.");
    }
}
