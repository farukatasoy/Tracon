using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The dashboard screen.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class DashboardTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Dashboard_charts_render_and_range_can_be_changed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        // Buckets are filled with zeros even for an empty store; charts still render.
        await Expect(session.Page.GetByTestId("timeseries-chart")).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("status-distribution-chart")).ToBeVisibleAsync();

        // 🚨 `.First` is REQUIRED: the empty-state text is shared by every chart
        // panel, and an empty dashboard now shows it in two of them (the model
        // breakdown and, since phase 68, the token breakdown). A bare
        // GetByText resolves to both and fails Playwright's strict mode.
        await Expect(session.Page.GetByText("No run in this window").First).ToBeVisibleAsync();

        // Changing the range must trigger a new /api/stats/timeseries request
        // (the 30d range switches from an hour bucket to a day bucket, to stay
        // under the 500-bucket limit).
        var timeseriesRefetched = session.Page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/stats/timeseries", StringComparison.Ordinal) &&
            response.Url.Contains("bucket=Day", StringComparison.Ordinal));

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "30d" }).ClickAsync();

        await timeseriesRefetched;

        await Expect(session.Page.GetByTestId("timeseries-chart")).ToBeVisibleAsync();
    }
}
