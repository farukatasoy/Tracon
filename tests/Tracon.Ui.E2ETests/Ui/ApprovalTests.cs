using System.Net.Http.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// Approval rules and the approvals inbox.
/// </summary>
public sealed class ApprovalTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Approval_rule_with_condition_is_created_and_shown()
    {
        // Phase 63: an admin-authored, argument-conditioned approval rule
        // ("amount <= 100") written from the screen, then read back — no
        // free-text expression box, the operator is a closed dropdown (K2).
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/mcp");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add rule" }).ClickAsync();

        await session.Page.GetByPlaceholder("refund_order").FillAsync("refund_order");

        await session.Page.GetByTestId("add-condition").ClickAsync();
        await session.Page.GetByTestId("condition-path-0").FillAsync("amount");
        await session.Page.GetByTestId("condition-operator-0").SelectOptionAsync("LessThanOrEqual");
        await session.Page.GetByTestId("condition-value-0").FillAsync("100");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(session.Page.GetByText("refund_order").First).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("amount ≤ 100")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Approvals_screen_shows_pending_request_and_run_completes_once_approved()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: services => services.UseScheduling(options =>
            {
                // The test does not need to wait in real time; the worker polls immediately.
                options.PollInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseDuration = TimeSpan.FromSeconds(10);
            }));
        await using var session = await Session.OpenAsync(browsers, host);

        // There is no way to trigger the Approvals screen THROUGH the UI
        // (queuing requires the 'Prefer: respond-async' header, not a form
        // submission) — so the pending request is seeded directly from the
        // API; what is actually under test is the Approvals screen ITSELF.
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };
        using var seedRequest = new HttpRequestMessage(HttpMethod.Post, "api/agents/approval-agent/run")
        {
            Content = JsonContent.Create(new { message = "cancel the order", sessionId = "e2e-approval-session" }),
        };
        seedRequest.Headers.Add("Prefer", "respond-async");

        using var seedResponse = await api.SendAsync(seedRequest);
        seedResponse.EnsureSuccessStatusCode();

        await session.Page.GotoAsync($"{host.UiAddress}/approvals");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Approvals", Exact = true })).ToBeVisibleAsync();

        // The worker leases the job from the queue and waits until it reaches
        // the tool call that requires approval; the screen auto-refreshes every 5 seconds.
        // 🚨 `Exact` matters from phase 175 on: the confirmation's own heading
        // reads "Approve cancel_order?", so a substring match resolves to two
        // elements the moment the dialog is up and Playwright's strict mode
        // rejects it. The row's cell is the exact string.
        await Expect(session.Page.GetByText("cancel_order", new() { Exact = true })).ToBeVisibleAsync();

        // Phase 175: the answer is no longer one click. Approving is the one
        // action in the console that earns a confirmation under criterion (b)
        // rather than (a) — nothing is destroyed, but K-368 means the decision
        // cannot be given twice, so the step is deliberate and this case walks
        // it rather than being loosened around it.
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();

        var confirm = session.Page.GetByTestId("confirm-decide");
        await Expect(confirm).ToBeVisibleAsync();

        // The consequence comes from the SAME message key the trigger's own
        // tooltip uses (§175.4 rule 2), so the sentence read before the click
        // and the sentence read inside the dialog cannot drift apart.
        await Expect(confirm.GetByText("queue a new run with the same session to continue.", new() { Exact = false })).ToBeVisibleAsync();

        await session.Page.GetByTestId("confirm-accept").ClickAsync();

        // The row disappears from the list: the request is no longer Pending.
        await Expect(session.Page.GetByText("cancel_order", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("Nothing is waiting")).ToBeVisibleAsync();

        // The decision queues a NEW run, which also completes. The Runs list
        // must show both the old row (which STAYS in AwaitingApproval, K-014)
        // and the new (Completed) row.
        await session.Page.GotoAsync($"{host.UiAddress}/runs");
        await Expect(session.Page.GetByText("awaiting approval", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("completed", new() { Exact = true }).First).ToBeVisibleAsync();
    }
}
