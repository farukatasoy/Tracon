using Microsoft.Playwright;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// The one place a test copies to the browser clipboard and reads it back.
/// </summary>
/// <remarks>
/// <para>
/// The clipboard belongs to the browser, not to a context: two tests that copy at the same time, each in a context of its
/// own, read each other's text. A run identifier test once read back a workflow diagram that another test had copied a
/// moment before (<see cref="ClipboardTests"/> shows the sharing without any other test in the way).
/// </para>
/// <para>
/// The lock covers the copy and the read and nothing else, so the rest of the two classes that copy keeps running in
/// parallel with everything.
/// </para>
/// </remarks>
internal static class SharedClipboard
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Runs <paramref name="copy"/> (a click on a copy control) and returns what the clipboard holds afterwards.</summary>
    /// <param name="page">A page of a context that was granted the clipboard permissions.</param>
    /// <param name="copy">The action that copies.</param>
    /// <returns>The clipboard text.</returns>
    public static async Task<string> CopyAndReadAsync(IPage page, Func<Task> copy)
    {
        await Gate.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            await copy();

            return await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        }
        finally
        {
            Gate.Release();
        }
    }
}
