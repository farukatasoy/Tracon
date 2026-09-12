using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.Ui.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace Tracon.Ui.E2ETests;

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
/// <c>TRACON_UI_SCREENSHOTS=1</c>, following the same refresh pattern as
/// <c>OpenApiSnapshotTests</c>. Without the variable a normal <c>dotnet test</c> stays
/// read-only and leaves no dirty files behind:
/// </para>
/// <code>
/// TRACON_UI_SCREENSHOTS=1 dotnet test tests/Tracon.Ui.E2ETests -c Release
/// </code>
/// <para>
/// 🚨 The browser locale is pinned to <c>en-US</c>. Since the console picks its
/// language from <c>navigator.language</c>, an unpinned run would produce Turkish
/// screenshots on a Turkish machine, and the documentation site is English only.
/// </para>
/// </remarks>
public sealed class DocumentationScreenshotTests(BrowserFixture browsers)
{
    private const string RefreshEnvVar = "TRACON_UI_SCREENSHOTS";

    /// <summary>Names the seed writes, used as landmarks that only their own screen shows.</summary>
    /// <remarks>
    /// A landmark that repeats the sidebar label ("Jobs") is present on every route
    /// and proves nothing. Each of these appears only once the screen has rendered
    /// the row the seed created.
    /// </remarks>
    private const string SeededSessionId = "support-ord-7";

    private const string SeededScheduleName = "nightly-summary";

    private const string SeededSkillName = "refund-policy";

    private const string SeededMcpServerName = "knowledge-base";

    private const string SeededTriggerName = "helpdesk-webhook";

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
        ("sessions", "/sessions", SeededSessionId),
        ("jobs", "/jobs", SeededScheduleName),
        ("workflows", "/workflows", "summarize-and-translate"),
        ("evals", "/evals", "Evals"),
        ("experiments", "/experiments", "Experiments"),
        ("approvals", "/approvals", "Order ORD-7"),
        ("tools", "/tools", "get_order_status"),
        ("skills", "/skills", SeededSkillName),
        ("models", "/models", "scripted"),
        ("mcp", "/mcp", SeededMcpServerName),
        ("triggers", "/triggers", SeededTriggerName),
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
        await SeedCatalogAsync(host);
        await SeedPendingApprovalAsync(host);

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

    /// <summary>
    /// Gives the catalog screens something to show. An empty list is a truthful
    /// picture of a fresh install and a useless one for the guide, so each screen the
    /// guide presents gets one representative row.
    /// </summary>
    private static async Task SeedCatalogAsync(UiHost host)
    {
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        await EnsureSuccessAsync(client.PutAsJsonAsync(
            $"{host.Prefix}/api/skills/{SeededSkillName}",
            new
            {
                name = SeededSkillName,
                description = "How to decide and word a refund.",
                instructions = "Check the order age, then state the decision in one sentence.",
                compatibility = "support",
                allowedTools = "get_order_status",
            }));

        await EnsureSuccessAsync(client.PutAsJsonAsync(
            $"{host.Prefix}/api/schedules/{SeededScheduleName}",
            new
            {
                // A batch over a list of inputs: the shape the screen describes, and
                // the one a schedule can fire on its own. AgentRun expects a caller
                // to supply the run id, so a timer cannot produce it.
                handlerKey = "tracon.agent-batch",
                targetName = "support",
                cron = "0 3 * * *",
                timeZone = "UTC",
                payload = new[] { "Summarise yesterday's unresolved tickets." },
                enabled = true,
            }));

        // Triggering it once turns the empty "Recent jobs" panel into a real row.
        await EnsureSuccessAsync(client.PostAsync(
            $"{host.Prefix}/api/schedules/{SeededScheduleName}/trigger",
            content: null));

        await WaitForJobToSettleAsync(client, host);

        await EnsureSuccessAsync(client.PutAsJsonAsync(
            $"{host.Prefix}/api/triggers/{SeededTriggerName}",
            new
            {
                targetKind = "Agent",
                targetName = "support",
                signingSecretConfigurationName = "Tracon:TriggerSecrets:Helpdesk",
                enabled = true,
            }));

        await EnsureSuccessAsync(client.PutAsJsonAsync(
            $"{host.Prefix}/api/mcp-servers/{SeededMcpServerName}",
            new
            {
                description = "Read-only company handbook.",
                endpoint = "https://mcp.example.invalid/sse",
                transport = "Sse",
                enabled = true,
                requiresApproval = true,
            }));
    }

    /// <summary>
    /// Waits until the queued job leaves its pending state, so the screenshot shows
    /// the same row on every run.
    /// </summary>
    /// <remarks>
    /// The worker polls on an interval. Capturing the screen without waiting produced
    /// a different picture per run — pending, running, or completed — and no assertion
    /// noticed. Failing here is better than publishing whichever the race produced.
    /// </remarks>
    private static async Task WaitForJobToSettleAsync(HttpClient client, UiHost host)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        string? status = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var document = JsonDocument.Parse(
                await client.GetStringAsync($"{host.Prefix}/api/jobs?limit=1"));

            var rows = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement
                : document.RootElement.GetProperty("items");

            if (rows.GetArrayLength() > 0)
            {
                status = rows[0].GetProperty("status").GetString();

                if (status is "Completed" or "Failed" or "Cancelled" or "Canceled")
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"The seeded job did not settle within two minutes; last status was '{status ?? "none"}'. " +
            "The screenshot would capture whichever state the race produced.");
    }

    /// <summary>
    /// Queues a run that stops at <c>AwaitingApproval</c>, so the approvals screen shows
    /// a real pending request instead of the empty state (phase 142). Only the queue
    /// path writes a mailbox row — a synchronous run's approval never reaches
    /// <c>GET /api/approvals/pending</c> at all.
    /// </summary>
    private static async Task SeedPendingApprovalAsync(UiHost host)
    {
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{host.Prefix}/api/agents/approval-agent/run")
        {
            // 🚨 A queued run without a sessionId is CORRECTED to Failed once it
            // asks for approval — the decision is the input of the next turn and
            // cannot be resolved without a session to carry it (AgentRunJobHandler).
            // Omitting it here would poll for AwaitingApproval for two minutes and
            // then fail on a status that was always going to be Failed.
            Content = JsonContent.Create(new { message = "Cancel order ORD-7.", sessionId = "approvals-ord-7" }),
            Headers = { { "Prefer", "respond-async" } },
        };

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = accepted.RootElement.GetProperty("runId").GetString();

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        string? status = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var document = JsonDocument.Parse(
                await client.GetStringAsync($"{host.Prefix}/api/runs/{runId}"));

            status = document.RootElement.GetProperty("status").GetString();

            if (string.Equals(status, "AwaitingApproval", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"The seeded approval did not reach AwaitingApproval within two minutes; " +
            $"last status was '{status ?? "none"}'.");
    }

    private static async Task EnsureSuccessAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Seeding '{response.RequestMessage?.RequestUri}' failed with " +
                $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
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

        // Both turns share one session id, so the session list and the conversation
        // history have something to show rather than a fresh-install empty state.
        const string SessionId = SeededSessionId;

        foreach (var message in new[] { "Where is order ORD-7?", "Thanks, that is all." })
        {
            using var response = await client.PostAsJsonAsync(
                $"{host.Prefix}/api/agents/support/run",
                new { message, sessionId = SessionId });

            response.EnsureSuccessStatusCode();

            await response.Content.ReadAsStringAsync();
        }
    }

    /// <summary>Resolves <c>docs-site/public/screenshots</c> from the test binary's location.</summary>
    private static string ResolveOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Tracon.slnx not found above the test binary.");

        return Path.Combine(root, "docs-site", "public", "screenshots");
    }
}
