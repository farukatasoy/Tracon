using System.Net.Http;
using System.Net.Http.Json;
using AgentPrism.Ui.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace AgentPrism.Ui.E2ETests;

/// <summary>
/// Produces the console screenshots used by the documentation site, and — on every
/// run — proves that each documented screen still renders.
/// </summary>
/// <remarks>
/// <para>
/// A screenshot taken by hand goes stale the moment the screen changes, and nobody
/// notices. Producing them from the E2E run removes that failure mode: the same
/// navigation that captures the image also asserts the screen's landmark element is
/// there, so a screen that stops rendering fails the build instead of leaving a
/// picture of a page that no longer exists.
/// </para>
/// <para>
/// The capture always happens; the WRITE is gated on
/// <c>AGENTPRISM_UI_SCREENSHOTS=1</c>, following the same refresh pattern as
/// <c>OpenApiSnapshotTests</c>. Without the variable a normal <c>dotnet test</c> stays
/// read-only and leaves no dirty files behind:
/// </para>
/// <code>
/// AGENTPRISM_UI_SCREENSHOTS=1 dotnet test tests/AgentPrism.Ui.E2ETests -c Release
/// </code>
/// <para>
/// 🚨 The browser locale is pinned to <c>en-US</c>. Since the console picks its
/// language from <c>navigator.language</c>, an unpinned run would produce Turkish
/// screenshots on a Turkish machine, and the documentation site is English only.
/// </para>
/// </remarks>
public sealed class DocumentationScreenshotTests(BrowserFixture browsers)
{
    private const string RefreshEnvVar = "AGENTPRISM_UI_SCREENSHOTS";

    /// <summary>The screens the UI guide shows, in the order the guide presents them.</summary>
    /// <remarks>
    /// Each entry is a route and the landmark that proves the screen actually rendered.
    /// A route whose landmark never appears fails the test rather than producing a
    /// picture of a spinner.
    /// </remarks>
    private static readonly (string Name, string Route, string Landmark)[] Screens =
    [
        ("dashboard", "/dashboard", "Dashboard"),
        ("agents", "/agents", "support"),
        ("agent-detail", "/agents/support", "Support assistant"),
        ("playground", "/playground", "Playground"),
        ("runs", "/runs", "Runs"),
        ("workflows", "/workflows", "summarize-and-translate"),
        ("evals", "/evals", "Evals"),
        ("experiments", "/experiments", "Experiments"),
        ("approvals", "/approvals", "Approvals"),
        ("tools", "/tools", "get_order_status"),
        ("models", "/models", "scripted"),
        ("audit", "/audit", "Audit"),
        ("diagnostics", "/diagnostics", "Diagnostics"),
        ("settings", "/settings", "Settings"),
    ];

    [Fact]
    public async Task Every_documented_screen_renders_and_is_captured()
    {
        await using var host = await UiHost.StartAsync();

        // A screenshot of an empty console teaches nothing. Two real runs — one plain,
        // one that calls a tool — give the dashboard, the run list, and the agent
        // detail screen something to show.
        await SeedRunsAsync(host);

        var context = await browsers.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseAddress,

            // The guide is read on a wide page; this is the same viewport the rest of
            // the E2E suite uses, so the screenshots match what the tests assert on.
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "en-US",

            // 🚨 The captures are light-theme on purpose. The console's default follows
            // the operating system, which would otherwise make the images depend on the
            // machine that produced them.
            ColorScheme = ColorScheme.Light,
            DeviceScaleFactor = 2,
        });

        await using (context.ConfigureAwait(false))
        {
            var page = await context.NewPageAsync();
            var directory = ResolveOutputDirectory();
            var write = string.Equals(
                Environment.GetEnvironmentVariable(RefreshEnvVar),
                "1",
                StringComparison.Ordinal);

            if (write)
            {
                Directory.CreateDirectory(directory);
            }

            foreach (var (name, route, landmark) in Screens)
            {
                await page.GotoAsync(host.UiAddress + route);

                await page.GetByText(landmark).First.WaitForAsync(new LocatorWaitForOptions
                {
                    Timeout = 15_000,
                });

                var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = write ? Path.Combine(directory, $"{name}.png") : null,
                    Type = ScreenshotType.Png,
                });

                bytes.Length.ShouldBeGreaterThan(
                    2_000,
                    $"The '{name}' screen ({route}) produced an implausibly small image; " +
                    "it probably rendered blank.");
            }
        }
    }

    /// <summary>Drives a couple of runs so the recorded-history screens have content.</summary>
    /// <remarks>
    /// The runs stream over SSE, so the response body is read to the end; returning
    /// before the stream closes would leave the run still in progress and the
    /// dashboard's counters at zero.
    /// </remarks>
    private static async Task SeedRunsAsync(UiHost host)
    {
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        foreach (var message in new[] { "Where is order ORD-7?", "Thanks, that is all." })
        {
            using var response = await client.PostAsJsonAsync(
                $"{host.Prefix}/api/agents/support/run",
                new { message });

            response.EnsureSuccessStatusCode();

            await response.Content.ReadAsStringAsync();
        }
    }

    /// <summary>Resolves <c>docs-site/public/screenshots</c> from the test binary's location.</summary>
    private static string ResolveOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("AgentPrism.slnx not found above the test binary.");

        return Path.Combine(root, "docs-site", "public", "screenshots");
    }
}
