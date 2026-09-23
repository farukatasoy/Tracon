using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The skills screen.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class SkillTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Skill_created_from_UI_is_listed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/skills/new");

        await session.Page.GetByPlaceholder("invoice-analysis").FillAsync("invoice-review");
        await session.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description" })
            .FillAsync("Reviews invoices.");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Skills" }).WaitForAsync();
        await session.Page.GetByText("invoice-review", new() { Exact = true }).WaitForAsync();
    }
}
