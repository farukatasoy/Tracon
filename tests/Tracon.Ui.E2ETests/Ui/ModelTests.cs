using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The models screen.
/// </summary>
[Collection(ConsoleScreens.Name)]
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
        await session.Page.GetByText("unknown", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });

        (await session.Page.GetByText("Provider connectivity checks are still missing").CountAsync()).ShouldBe(0);

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Check now" }).First.ClickAsync();

        await session.Page.GetByText("unknown", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });
    }
}
