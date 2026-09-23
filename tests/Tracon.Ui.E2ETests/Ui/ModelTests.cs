using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The models screen.
/// </summary>
public sealed class ModelTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Models_screen_shows_health_badge()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/models");

        // FakeModelProvider does not implement IModelProviderHealthCheck; this
        // is not a bug and the badge must show "unknown". The leftover
        // "provider connectivity checks are still missing" note from Phase 6
        // must no longer appear on screen.
        await Expect(session.Page.GetByText("unknown", new() { Exact = true }).First).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("Provider connectivity checks are still missing")).ToHaveCountAsync(0);

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Check now" }).First.ClickAsync();

        await Expect(session.Page.GetByText("unknown", new() { Exact = true }).First).ToBeVisibleAsync();
    }
}
