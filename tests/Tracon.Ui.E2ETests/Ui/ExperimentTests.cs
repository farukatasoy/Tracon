using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Tracon.Ui.E2ETests.Infrastructure.UiTestHelpers;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The experiments screen.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class ExperimentTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Experiment_is_created_started_and_traffic_reflects_in_results_table()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await CreateAgentWithTwoVersionsAsync(host, session, "exp-agent", "v1 instructions", "v2 instructions");

        await session.Page.GotoAsync($"{host.UiAddress}/experiments");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Experiments" }).First.WaitForAsync();

        await session.Page.GetByTestId("new-experiment").ClickAsync();
        await session.Page.GetByTestId("experiment-name").FillAsync("e2e-experiment");
        await session.Page.GetByTestId("experiment-agent-name").FillAsync("exp-agent");

        // The version dropdown fills from the agentVersions query once the agent name is entered.
        await session.Page.GetByTestId("variant-name-0").FillAsync("control");
        await session.Page.GetByTestId("variant-version-0").SelectOptionAsync("1");
        await session.Page.GetByTestId("variant-weight-0").FillAsync("50");

        await session.Page.GetByTestId("variant-name-1").FillAsync("v2");
        await session.Page.GetByTestId("variant-version-1").SelectOptionAsync("2");
        await session.Page.GetByTestId("variant-weight-1").FillAsync("50");

        await session.Page.GetByTestId("experiment-save").ClickAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "e2e-experiment" }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "e2e-experiment" }).WaitForAsync();

        await session.Page.GetByTestId("experiment-start").ClickAsync();
        await session.Page.GetByText("running", new() { Exact = true }).WaitForAsync(new() { Timeout = 10_000 });

        // A request is sent to the agent while the experiment is running;
        // since assignment is deterministic, this run is written to one of
        // the two arms.
        await session.Page.GotoAsync($"{host.UiAddress}/playground/exp-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await session.Page.GetByText("Echo: hello").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/experiments/e2e-experiment");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "e2e-experiment" }).WaitForAsync();

        // The results table shows raw counts; at least one column must show a
        // total of 1 run. There is no statistical "winner" claim anywhere.
        await session.Page.GetByRole(AriaRole.Cell, new() { Name = "1", Exact = true }).First
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByTestId("experiment-stop").ClickAsync();
        await session.Page.GetByText("stopped", new() { Exact = true }).WaitForAsync(new() { Timeout = 10_000 });
    }
}
