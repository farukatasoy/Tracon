using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// A test that fails on purpose, to prove a failed E2E test leaves a browser trace and the page it was on.
/// </summary>
/// <remarks>
/// Skipped unless <see cref="EvidenceSelfTest.Variable"/> is set. CI sets it in one step, expects the test to be red and
/// looks for the trace and for the page note in the evidence directory.
/// </remarks>
public sealed class PageFailureEvidenceTests(BrowserFixture browsers)
{
    [Fact]
    public async Task A_failed_page_test_leaves_its_trace_and_the_page_it_was_on()
    {
        EvidenceSelfTest.SkipUnlessEnabled();

        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/audit");

        throw new InvalidOperationException("The page was reached and the test fails on purpose.");
    }
}
