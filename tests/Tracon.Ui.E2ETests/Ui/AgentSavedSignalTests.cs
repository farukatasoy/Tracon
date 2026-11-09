using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The signal that tells a test an agent save has finished.
/// </summary>
public sealed class AgentSavedSignalTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Saved_signal_is_not_met_on_the_editor_while_the_new_version_is_still_in_flight()
    {
        // A wait for a heading named after the agent used to end the shared
        // two-version helper. The editor's own title is "Edit {name}", and a
        // role locator's Name matches a substring, so that wait was met while
        // the second save was still on the wire. The save is held here with a
        // gate, which makes "in flight" a state the test owns instead of a
        // race it hopes to win; every assertion below reads that state at once.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        var page = session.Page;
        const string name = "in-flight-agent";

        await CreateAgentAsync(host, session, name, "first instructions");
        await EditInstructionsAsync(session, "second instructions");

        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await page.RouteAsync(
            $"**/api/agents/{name}",
            async route =>
            {
                if (string.Equals(route.Request.Method, "PUT", StringComparison.Ordinal))
                {
                    held.TrySetResult();
                    await release.Task;
                }

                await route.ContinueAsync();
            });

        try
        {
            await page.GetByTestId("agent-save").ClickAsync();
            await held.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The editor is still on screen and the request has not been answered.
            page.Url.ShouldEndWith($"/agents/{name}/edit");

            // The wait the helper used to end on: a substring match on the heading.
            (await page.GetByRole(AriaRole.Heading, new() { Name = name }).First.CountAsync())
                .ShouldBe(1, "The editor's own title no longer contains the agent name, so this test no longer shows the old wait being met early.");

            // The signal the helper waits on now. A count read, not a wait for absence.
            (await AgentSavedMarker(page, name, 2).CountAsync())
                .ShouldBe(0, "The saved signal was met on the editor while the save request was still held.");
        }
        finally
        {
            release.TrySetResult();
        }

        await WaitForAgentSavedAsync(page, name, 2);

        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        var versions = await api.GetFromJsonAsync<JsonElement>(
            $"api/agents/{name}/versions",
            TestContext.Current.CancellationToken);

        versions.GetArrayLength().ShouldBe(2);
    }
}
