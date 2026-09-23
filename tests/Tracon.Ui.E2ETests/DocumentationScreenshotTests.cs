using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

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

    /// <summary>
    /// The failure text the run-detail capture is recognised by.
    /// </summary>
    /// <remarks>
    /// 🚨 NOT the message the tool threw. <c>ToolFailureText</c> keeps an arbitrary
    /// tool exception message out of persistent output — only a
    /// <c>TraconException</c> survives verbatim — so what reaches the screen is the
    /// exception's type name in a fixed sentence. Writing the thrown message here
    /// waits fifteen seconds for text that can never render.
    /// </remarks>
    private const string FailedToolMessage = "Tool failed with InvalidOperationException";

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

        var directory = ResolveOutputDirectory();
        var write = string.Equals(
            Environment.GetEnvironmentVariable(RefreshEnvVar),
            "1",
            StringComparison.Ordinal);

        if (write)
        {
            Directory.CreateDirectory(directory);
        }

        // 🚨 BOTH themes are captured, and the site shows the OPPOSITE one: a dark
        // page embeds the light capture and a light page embeds the dark one, so the
        // screenshot reads as an object on the page instead of dissolving into it.
        // The theme is pinned rather than left to the machine — an unpinned run would
        // produce whatever the developer's OS prefers.
        // The guide shows one screen that only exists once something has run: its
        // route carries an id the seed produced, so it cannot live in the static
        // table above, which holds fixed routes only.
        var failedRunId = await SeedFailedRunAsync(host);

        var screens = new List<(string Name, string Route, string Landmark)>(Screens)
        {
            ("run-detail", $"/runs/{failedRunId}", FailedToolMessage),
        };

        foreach (var (theme, suffix) in new[] { ("dark", string.Empty), ("light", "-light") })
        {
            await CaptureAsync(host, theme, suffix, directory, write, screens);
        }
    }

    /// <summary>Captures every documented screen in one colour scheme.</summary>
    /// <remarks>
    /// The dark pass writes <c>&lt;name&gt;.png</c> and the light pass
    /// <c>&lt;name&gt;-light.png</c>, so the dark file name stays the one every
    /// existing page already references.
    /// </remarks>
    private async Task CaptureAsync(
        UiHost host,
        string theme,
        string suffix,
        string directory,
        bool write,
        IReadOnlyList<(string Name, string Route, string Landmark)> screens)
    {
        var context = await browsers.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = host.BaseAddress,

            // The guide is read on a wide page; this is the same viewport the rest of
            // the E2E suite uses, so the screenshots match what the tests assert on.
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "en-US",
            DeviceScaleFactor = 2,
        });

        await using (context.ConfigureAwait(false))
        {
            // 🚨 Playwright's ColorScheme does NOT move the console. The console
            // defaults to dark outright and consults prefers-color-scheme only when
            // the stored preference is "system", so the capture has to write that
            // preference itself. Seeded before any page script runs, so the first
            // paint is already the theme being captured.
            await context.AddInitScriptAsync(
                "try { window.localStorage.setItem('tracon.theme', '" +
                theme +
                "'); } catch { }");

            var page = await context.NewPageAsync();

            foreach (var (name, route, landmark) in screens)
            {
                await page.GotoAsync(host.UiAddress + route);

                await page.GetByText(landmark).First.WaitForAsync(new LocatorWaitForOptions
                {
                    Timeout = 15_000,
                });

                var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = write ? Path.Combine(directory, $"{name}{suffix}.png") : null,
                    Type = ScreenshotType.Png,
                });

                bytes.Length.ShouldBeGreaterThan(
                    2_000,
                    $"The '{name}' screen ({route}) produced an implausibly small image " +
                    $"in the {theme} theme; it probably rendered blank.");
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
        => await WaitUntil.ValueAsync(
            async () =>
            {
                using var document = JsonDocument.Parse(
                    await client.GetStringAsync($"{host.Prefix}/api/jobs?limit=1"));

                var rows = document.RootElement.ValueKind == JsonValueKind.Array
                    ? document.RootElement
                    : document.RootElement.GetProperty("items");

                return rows.GetArrayLength() > 0 ? rows[0].GetProperty("status").GetString() : null;
            },
            static status => status is "Completed" or "Failed" or "Cancelled" or "Canceled",
            "the seeded job to settle (otherwise the screenshot captures whichever state the race produced)",
            TimeSpan.FromMinutes(2));

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

        await WaitUntil.ValueAsync(
            async () =>
            {
                using var document = JsonDocument.Parse(
                    await client.GetStringAsync($"{host.Prefix}/api/runs/{runId}"));

                return document.RootElement.GetProperty("status").GetString();
            },
            static status => string.Equals(status, "AwaitingApproval", StringComparison.Ordinal),
            "the seeded approval to reach AwaitingApproval",
            TimeSpan.FromMinutes(2));
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

    /// <summary>
    /// Runs the agent whose tool always throws, and returns that run's id.
    /// </summary>
    /// <remarks>
    /// 🚨 A throwing tool does NOT fail the run. The exception is recorded as a
    /// <c>ToolFailed</c> event on the call, the model receives the error as that
    /// call's result, and the turn then finishes normally — so this run ends
    /// Completed while carrying a failed tool call. That is the scenario the tour
    /// shows, and requiring a Failed status here is what made the first attempt
    /// throw. What proves the capture is <see cref="FailedToolMessage"/>: if that
    /// text is not on the detail screen, the screenshot wait times out and the test
    /// fails rather than writing a picture of the wrong thing.
    /// </remarks>
    private static async Task<string> SeedFailedRunAsync(UiHost host)
    {
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using var response = await client.PostAsJsonAsync(
            $"{host.Prefix}/api/agents/fulfilment/run",
            new { message = "Reserve stock for order ORD-9.", sessionId = "fulfilment-ord-9" });

        // The run itself fails; the HTTP call that carried it does not have to.
        await response.Content.ReadAsStringAsync();

        // The run id is read back from the list rather than the run response, so this
        // does not depend on which envelope a failed synchronous run returns.
        // GET /api/runs returns IReadOnlyList<RunRecord>, so the root is the array
        // itself - TryGetProperty THROWS on an array rather than returning false,
        // which is how a defensive guard here failed on the real shape.
        using var runs = JsonDocument.Parse(
            await client.GetStringAsync($"{host.Prefix}/api/runs"));

        foreach (var run in runs.RootElement.EnumerateArray())
        {
            if (string.Equals(run.GetProperty("agentName").GetString(), "fulfilment", StringComparison.Ordinal))
            {
                return run.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("The fulfilment run carried no id.");
            }
        }

        throw new InvalidOperationException(
            "No run was recorded for 'fulfilment'. The run-detail capture has nothing to show.");
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
