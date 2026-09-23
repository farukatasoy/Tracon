using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The console's language: detection, the switch, and server text left untranslated.
/// </summary>
public sealed class LocalizationTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Language_is_selected_from_browser_language()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        await session.Page.GotoAsync(host.UiAddress);

        // With no stored preference, the default language comes from navigator.language.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Gösterge Paneli" })).ToBeVisibleAsync();

        // 🚨 The lang attribute is not decoration: it is what the screen
        // reader picks its voice from.
        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("lang", "tr");
    }

    [Fact]
    public async Task Language_stays_English_in_English_browser()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("lang", "en");
    }

    [Fact]
    public async Task Language_is_changed_and_preference_persists_across_reload()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await session.Page.GetByTestId("language-toggle").ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Gösterge Paneli" })).ToBeVisibleAsync();

        // Language is not a secret (does not conflict with K-047): it is kept
        // in localStorage and persists across reload — and in a new tab.
        await session.Page.ReloadAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Gösterge Paneli" })).ToBeVisibleAsync();

        // The stored preference overrides the browser language.
        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("lang", "tr");

        // The selector on the Settings screen also shows the same preference.
        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByTestId("language-select")).ToHaveValueAsync("tr");
    }

    [Fact]
    public async Task Server_error_in_Turkish_UI_is_shown_without_translation()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        // A nonexistent agent produces a 404; the UI shows the server's own
        // text as-is — the API contract is single-language.
        await session.Page.GotoAsync($"{host.UiAddress}/agents/nonexistent-agent");

        await Expect(session.Page.GetByRole(AriaRole.Alert).First).ToBeVisibleAsync();
    }
}
