using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The command palette and the keyboard shortcuts.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class KeyboardTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Command_palette_navigates_to_screen()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("Control+k");
        await Expect(session.Page.GetByTestId("command-palette")).ToBeVisibleAsync();

        await session.Page.Keyboard.TypeAsync("sessions");
        await session.Page.Keyboard.PressAsync("Enter");

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Sessions" })).ToBeVisibleAsync();

        // The palette closes after navigating.
        await Expect(session.Page.GetByTestId("command-palette")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Command_palette_closes_with_Esc()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("Control+k");
        await Expect(session.Page.GetByTestId("command-palette")).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("Escape");

        await Expect(session.Page.GetByTestId("command-palette")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Command_palette_returns_focus_to_opening_button_after_Esc()
    {
        // BUG-S4-004: Esc closed the palette but never returned focus
        // anywhere (document.activeElement fell back to <body>).
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.GetByTestId("palette-open").ClickAsync();
        await Expect(session.Page.GetByTestId("command-palette")).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("Escape");

        await Expect(session.Page.GetByTestId("command-palette")).ToHaveCountAsync(0);

        var focusIsOnTrigger = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector('[data-testid=\"palette-open\"]')");

        focusIsOnTrigger.ShouldBeTrue("Focus did not return to the button that opened the palette after Esc.");
    }

    [Fact]
    public async Task Command_palette_is_filtered_by_role()
    {
        // When the Admin policy fails, the "New agent" command must also
        // disappear from the palette: offering an action the server will
        // reject is worse than not offering it. Hiding it is still a
        // courtesy; the server is what enforces it (Phase 9).
        await using var host = await UiHost.StartAsync(
            configureServices: services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, policy => policy.RequireAssertion(_ => false)));

        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("Control+k");
        await Expect(session.Page.GetByTestId("command-palette")).ToBeVisibleAsync();

        await session.Page.Keyboard.TypeAsync("new agent");

        await Expect(session.Page.GetByText("Nothing matches that.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task G_A_shortcut_navigates_to_agents_screen()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("g");
        await session.Page.Keyboard.PressAsync("a");

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Shortcut_does_not_trigger_inside_text_field()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync($"{host.UiAddress}/playground");
        await Expect(session.Page.GetByTestId("playground-input")).ToBeVisibleAsync();

        // 🚨 A key typed into a text field belongs to that text field. "ga"
        // here is two letters, not a navigation shortcut.
        await session.Page.GetByTestId("playground-input").ClickAsync();
        await session.Page.Keyboard.TypeAsync("gargle");

        await Expect(session.Page.GetByTestId("playground-input")).ToHaveValueAsync("gargle");

        // The screen must not have changed.
        session.Page.Url.ShouldContain("/playground");
    }

    [Fact]
    public async Task Shortcut_help_opens_with_question_mark()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("?");

        await Expect(session.Page.GetByTestId("shortcut-help")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Keyboard shortcuts").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Command_palette_takes_focus_traps_Tab_and_returns_focus_to_its_trigger_on_Escape()
    {
        // The four things a hand-written modal forgets. `dialog.tsx` owns all
        // four for every overlay in the console; this is the proof on the one
        // an operator opens most.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();

        await session.Page.GetByTestId("palette-open").ClickAsync();
        await Expect(session.Page.GetByTestId("command-palette")).ToBeVisibleAsync();

        // 1. Focus moved INTO the dialog, onto its one control.
        (await session.Page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('role') ?? ''"))
            .ShouldBe("combobox");

        // 2. Tab cannot leave it.
        await session.Page.Keyboard.PressAsync("Tab");
        await session.Page.Keyboard.PressAsync("Tab");

        (await session.Page.EvaluateAsync<bool>(
                "() => document.querySelector('[data-testid=\"command-palette\"]')?.contains(document.activeElement) === true"))
            .ShouldBeTrue("Tab escaped the command palette.");

        // 3. Escape closes it, and 4. focus goes back to the button that opened it.
        await session.Page.Keyboard.PressAsync("Escape");
        await Expect(session.Page.GetByTestId("command-palette")).ToHaveCountAsync(0);

        (await session.Page.EvaluateAsync<string>(
                "() => document.activeElement?.getAttribute('data-testid') ?? ''"))
            .ShouldBe("palette-open", "Focus did not return to the element that opened the palette.");
    }

    [Fact]
    public async Task Shortcut_help_dialog_opens_with_the_question_mark_and_closes_on_Escape()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();

        await session.Page.Keyboard.PressAsync("?");

        var dialog = session.Page.GetByTestId("shortcut-help");
        await Expect(dialog).ToBeVisibleAsync();

        await Expect(dialog).ToHaveAttributeAsync("aria-modal", "true");

        // The dialog names itself through its heading, not a bare aria-label.
        (await dialog.GetAttributeAsync("aria-labelledby")).ShouldNotBeNullOrEmpty();

        (await session.Page.EvaluateAsync<bool>(
                "() => document.querySelector('[data-testid=\"shortcut-help\"]')?.contains(document.activeElement) === true"))
            .ShouldBeTrue("Opening the cheat sheet did not move focus into it.");

        await session.Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
    }
}
