using Microsoft.Playwright;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// The browser instance shared by all UI tests.
/// </summary>
/// <remarks>
/// <para>
/// Playwright's browser binaries do not ship with the NuGet package. If
/// missing, the fixture downloads them itself, so <c>dotnet test</c> runs on
/// any machine without an extra setup step. The first run is therefore slow;
/// later runs use the downloaded binary.
/// </para>
/// <para>
/// Only Chromium is downloaded. All three browsers together cost far more to
/// download than the UI itself, and these tests validate the UI's behavior,
/// not browser differences.
/// </para>
/// </remarks>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    /// <summary>The browser kept open for the duration of the test run.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Playwright could not download Chromium (exit code {exitCode}). " +
                "On Linux, system dependencies may also be needed: playwright install --with-deps chromium");
        }

        _playwright = await Playwright.CreateAsync();

        // 🚨 Fake media device: voice mode tests cannot find a real microphone.
        // Both flags are required together — one produces the device, the other
        // suppresses the permission prompt. The flags are on for every test, but
        // only affect tests that call getUserMedia.
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args =
            [
                "--use-fake-device-for-media-stream",
                "--use-fake-ui-for-media-stream",
                "--autoplay-policy=no-user-gesture-required",
            ],
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
