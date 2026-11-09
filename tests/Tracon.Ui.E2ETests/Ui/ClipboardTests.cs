using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// What the tests that read the clipboard rest on: it is shared by every context of one browser.
/// </summary>
/// <param name="browsers">The browser shared by the UI tests.</param>
public sealed class ClipboardTests(BrowserFixture browsers)
{
    [Fact]
    public async Task The_clipboard_is_shared_by_every_context_of_one_browser()
    {
        await using var host = await UiHost.StartAsync();
        await using var writer = await Session.OpenAsync(browsers, host);
        await using var reader = await Session.OpenAsync(browsers, host);

        await writer.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await reader.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await writer.Page.GotoAsync($"{host.UiAddress}/");
        await reader.Page.GotoAsync($"{host.UiAddress}/");

        // Takes the lock the copying tests take: this test must not change the clipboard under one of them.
        var seen = await SharedClipboard.CopyAndReadAsync(
            reader.Page,
            async () => await writer.Page.EvaluateAsync("() => navigator.clipboard.writeText('written by another context')"));

        seen.ShouldBe(
            "written by another context",
            "The clipboard is no longer shared across contexts: the lock in SharedClipboard is not needed and can go.");
    }
}
