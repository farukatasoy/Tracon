using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The jobs screen: schedules and batch runs.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class JobTests(BrowserFixture browsers)
{
    /// <summary>Pattern that matches the job detail heading.</summary>
    private static readonly Regex JobHeadingPattern =
        new("^Job ", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Schedule_is_created_triggered_and_job_completes()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: services => services.UseScheduling(options =>
            {
                // The test does not need to wait in real time; the worker polls immediately.
                options.PollInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseDuration = TimeSpan.FromSeconds(10);
            }));
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/jobs");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Jobs", Exact = true })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "New schedule" }).ClickAsync();

        await session.Page.GetByPlaceholder("nightly-report").FillAsync("e2e-batch-job");

        // Phase 137: the handler is picked from the server's own allow-list, not
        // from two hard-coded options. Leaving it blank is a 400, so the screen
        // must actually offer the built-in key here.
        await session.Page.GetByTestId("schedule-handler-key").SelectOptionAsync(JobHandlerKeys.AgentBatch);

        await session.Page.GetByPlaceholder("summarizer").FillAsync("support");
        // Phase 129: the lane field. Left blank on a fresh row, "default" is
        // shown; here it is set so the column's actual value can be asserted.
        await session.Page.GetByPlaceholder("default", new() { Exact = true }).FillAsync("media");
        await session.Page.Locator("textarea").FillAsync("[\"hello\"]");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(session.Page.GetByText("e2e-batch-job")).ToBeVisibleAsync();

        // The schedule row's lane column shows the value just saved.
        await Expect(session.Page.Locator("table").First.Locator("tbody tr", new() { HasText = "e2e-batch-job" })
            .GetByText("media", new() { Exact = true })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Trigger" }).ClickAsync();

        // The worker leases and runs the job on its fast poll interval; the
        // completion badge appears in the "Recent jobs" panel.
        await Expect(session.Page.GetByText("completed", new() { Exact = true }).First).ToBeVisibleAsync();

        // The triggered job's own row also carries the schedule's lane
        // (129.1: a cron/trigger-dispatched job inherits it from the schedule).
        await Expect(session.Page.Locator("table").Last.Locator("tbody tr").First
            .GetByText("media", new() { Exact = true })).ToBeVisibleAsync();

        // Navigate to the job detail: the item input and run link appear.
        // "Recent jobs" is the SECOND table on the page (Schedules comes
        // first); the first row's link goes to the job id.
        await session.Page.Locator("table").Last.Locator("tbody tr").First
            .GetByRole(AriaRole.Link).First.ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { NameRegex = JobHeadingPattern })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("hello")).ToBeVisibleAsync();
    }
}
