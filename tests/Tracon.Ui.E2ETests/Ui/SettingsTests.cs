using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The settings screen: theme, quotas and webhooks.
/// </summary>
public sealed class SettingsTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Dark_theme_toggle_works_and_persists()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        var before = await session.Page.GetAttributeAsync("html", "data-theme");

        await session.Page.GetByTestId("theme-toggle").ClickAsync();

        // Phase 184: read once the theme has moved, not the instant the click
        // returns - the repaint is the page's own work.
        await Expect(session.Page.Locator("html"), "The theme did not change.")
            .Not.ToHaveAttributeAsync("data-theme", before ?? string.Empty);

        var after = await session.Page.GetAttributeAsync("html", "data-theme");

        (after is "light" or "dark").ShouldBeTrue($"Unexpected theme: {after}");

        // The preference lives in localStorage; a reload must preserve it.
        await session.Page.ReloadAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await Expect(session.Page.Locator("html"), "The theme preference was not preserved across reload.")
            .ToHaveAttributeAsync("data-theme", after!);
    }

    [Fact]
    public async Task Theme_selector_in_settings_updates_top_bar_toggle_in_same_session()
    {
        // BUG-S4-006: the <select> on the Settings screen applied the theme
        // correctly (<html data-theme> really changed), but the top bar's
        // ThemeToggle never learned about its own separate useState — it kept
        // showing the wrong icon/title (title="Theme: dark") for the rest of
        // the SPA session, until a full page reload.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        // First switch to "dark" via the top bar toggle: the "light" selection
        // below is then a real change, independent of this machine's system
        // color preference.
        if (!string.Equals(await session.Page.GetAttributeAsync("html", "data-theme"), "dark", StringComparison.Ordinal))
        {
            await session.Page.GetByTestId("theme-toggle").ClickAsync();
        }

        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        // The wrapping <label>'s accessible name concatenates the <select>'s
        // own rendered option text ("ThemeFollow systemLightDark"), so
        // GetByLabel("Theme") never matches exactly; scope by the wrapper
        // instead.
        await session.Page.Locator("label", new() { HasText = "Theme" }).Locator("select")
            .SelectOptionAsync("light");

        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");

        // 🚨 Read through the BINDING, not off a `title` attribute. Phase 165
        // moved this description into `Tooltip`, because `title` never showed
        // the current preference to a touch or keyboard user at all. The
        // assertion got stronger in the move: it now proves the text is
        // attached to the control (`aria-describedby`) rather than merely
        // present somewhere on it.
        var description = await session.Page.GetByTestId("theme-toggle").GetAttributeAsync("aria-describedby");

        description.ShouldNotBeNullOrEmpty("The toggle carries no description to read.");

        (await session.Page.Locator($"#{description}").InnerTextAsync())
            .ShouldBe("Theme: light", "The top bar toggle did not learn about the change made in Settings.");
    }

    [Fact]
    public async Task Quota_is_added_and_usage_bar_appears()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add quota" }).ClickAsync();
        await session.Page.GetByTestId("quota-max-runs").FillAsync("100");
        await session.Page.GetByTestId("quota-save").ClickAsync();

        // Once the rule is saved, the row and the usage bar appear.
        await Expect(session.Page.GetByTestId("quota-row").First).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("quota-bar").First).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("0 / 100", new() { Exact = false }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Webhook_is_added_and_test_send_completes()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add webhook" }).ClickAsync();
        await session.Page.GetByTestId("webhook-name").FillAsync("order-service");
        await session.Page.GetByTestId("webhook-url").FillAsync("https://example.com/hooks/tracon");

        // 🚨 What goes here is NOT a secret, it is the NAME of the key the
        // secret will be read from.
        await session.Page.GetByTestId("webhook-secret-key")
            .FillAsync("Tracon:WebhookSecrets:order-service");

        await session.Page.GetByTestId("webhook-save").ClickAsync();

        await Expect(session.Page.GetByTestId("webhook-row").First).ToBeVisibleAsync();

        // The key's name appears on screen; its value never does.
        await Expect(session.Page.GetByText("Tracon:WebhookSecrets:order-service", new() { Exact = false })
            .First).ToBeVisibleAsync();

        await session.Page.GetByTestId("webhook-test").First.ClickAsync();
        await Expect(session.Page.GetByTestId("webhook-test-result")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Console_opens_dark_and_still_follows_the_system_when_asked_to()
    {
        // The console is an operations surface, so dark is the product's answer
        // and not the machine's: a console with nothing stored opens dark even
        // on a machine that reports a light system preference. Choosing "follow
        // system" in Settings puts the machine back in charge.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, colorScheme: ColorScheme.Light);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings", Exact = true })).ToBeVisibleAsync();

        await Expect(session.Page.Locator("html"), "A console with no stored preference did not open dark.").ToHaveAttributeAsync("data-theme", "dark");

        // The wrapping <label>'s accessible name concatenates the <select>'s own
        // rendered option text, so GetByLabel("Theme") never matches exactly;
        // scope by the wrapper instead.
        await session.Page.Locator("label", new() { HasText = "Theme" }).Locator("select")
            .SelectOptionAsync("system");

        await Expect(session.Page.Locator("html"), "Following the system no longer follows the system.").ToHaveAttributeAsync("data-theme", "light");
    }
}
