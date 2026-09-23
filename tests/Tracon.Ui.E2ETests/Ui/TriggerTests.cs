using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The triggers screen.
/// </summary>
public sealed class TriggerTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Trigger_created_from_UI_is_listed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/triggers/new");

        await session.Page.GetByRole(AriaRole.Textbox, new() { Name = "Name *", Exact = true })
            .FillAsync("slack-e2e");
        await session.Page.GetByPlaceholder("demo").FillAsync("support");
        await session.Page.GetByPlaceholder("Tracon:TriggerSecrets:Slack")
            .FillAsync("Tracon:TriggerSecrets:SlackE2E");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Triggers" })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("slack-e2e", new() { Exact = true })).ToBeVisibleAsync();
    }
}
