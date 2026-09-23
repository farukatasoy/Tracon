using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The evals screen.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class EvalTests(BrowserFixture browsers)
{
    /// <summary>Pattern that matches the eval run detail heading.</summary>
    private static readonly Regex EvalRunHeadingPattern =
        new("^Eval run ", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Eval_suite_is_created_case_added_and_run_passes()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: services => services.UseScheduling(options =>
            {
                // The test does not need to wait in real time; the worker polls immediately.
                options.PollInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseDuration = TimeSpan.FromSeconds(10);
            }));
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/evals");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Evals", Exact = true }).WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "New suite" }).ClickAsync();

        await session.Page.GetByPlaceholder("customer-support-suite").FillAsync("e2e-eval-suite");
        // The "support" scripted model runs without a tool and reflects the
        // input verbatim as "Echo: {query}" (FakeModelProvider) — so the
        // default nonEmpty check reliably passes.
        await session.Page.GetByPlaceholder("customer-support-agent").FillAsync("support");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "e2e-eval-suite" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "e2e-eval-suite", Exact = true })
            .WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add case" }).ClickAsync();
        var query = session.Page.GetByPlaceholder("What is your return policy?");
        await Assertions.Expect(query).ToBeEditableAsync();
        await query.FillAsync("Where is my order, can you help?");

        var casesSaved = session.Page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/evals/e2e-eval-suite/cases", StringComparison.Ordinal) &&
            string.Equals(response.Request.Method, "PUT", StringComparison.Ordinal));
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save cases" }).ClickAsync();
        await casesSaved;

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Run now" }).ClickAsync();

        // The worker leases and runs the job on its fast poll interval; the
        // completion badge appears in the "Runs" panel.
        await session.Page.GetByText("completed", new() { Exact = true })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.Locator("table").Last.Locator("tbody tr").First
            .GetByRole(AriaRole.Link).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { NameRegex = EvalRunHeadingPattern })
            .WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("passed", new() { Exact = true }).WaitForAsync();
    }
}
