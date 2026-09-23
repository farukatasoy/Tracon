using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// The helpers more than one console test class uses.
/// </summary>
/// <remarks>
/// Phase 184 split <c>UiTests.cs</c> into one class per screen. These members
/// moved here unchanged except for their visibility; a class reaches them
/// through <c>using static</c>, so its test bodies read exactly as before.
/// </remarks>
internal static class UiTestHelpers
{
    /// <summary>Pattern that matches the run link on the Playground screen.</summary>
    internal static readonly Regex RunLinkPattern =
        new("^run ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Creates a database agent from the UI, then edits its instructions to
    /// produce a second version. Shared precondition for the diff and
    /// experiment tests.
    /// </summary>
    internal static async Task WaitForDefaultProviderAsync(IPage page)
    {
        // The editor renders before its provider catalogue query completes.
        // Filling the other fields proves neither that the query settled nor
        // that the form is valid; under CI load the Save button can therefore
        // remain disabled until Playwright's unrelated click timeout expires.
        await Expect(page.GetByTestId("agent-provider"))
            .ToHaveValueAsync(ScriptedModels.ProviderName, new() { Timeout = 60_000 });
    }

    internal static async Task CreateAgentWithTwoVersionsAsync(
        UiHost host,
        Session session,
        string name,
        string firstInstructions,
        string secondInstructions)
    {
        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);
        await session.Page.GetByTestId("agent-name").FillAsync(name);
        await session.Page.GetByTestId("agent-instructions").FillAsync(firstInstructions);
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        var save = session.Page.GetByTestId("agent-save");
        await Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await session.Page.GetByTestId("agent-instructions").FillAsync(secondInstructions);
        save = session.Page.GetByTestId("agent-save");
        await Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Verifies that the page itself does not shift horizontally at 375px width.
    /// BUG-S4-008: because <c>Panel</c>'s title+action row and <c>Row</c>'s
    /// value column (long URL values) did not wrap, the page as a whole
    /// widened on a narrow screen — not the tables/cards inside it.
    /// </summary>
    internal static async Task AssertNoHorizontalOverflowAsync(IPage page, string screen)
    {
        // Park the pointer first, so a tooltip the mouse happens to rest on
        // after the previous step's click is not part of what this measures.
        // A tooltip's own layout has its own case
        // (`A_tooltip_near_the_right_edge_stays_inside_the_viewport`).
        //
        // 🚨 This was NOT the cause of this check's intermittency, and the
        // record is worth keeping: `Proof_slice_screens_...` failed one run in
        // three and passed every time in isolation, and parking the pointer did
        // not change that. The real culprit was a 32-character trace id inside
        // a flex wrapper with no `min-w-0` (`Mono`'s copy wrapper) — and the
        // probe below NAMED it, once a failing run was actually captured
        // instead of reasoned about. A flaky overflow check means the element
        // that overflows is rendered conditionally; here the waterfall only
        // renders when a span was sampled.
        await page.Mouse.MoveAsync(0, 0);

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        if (overflow <= 0)
        {
            return;
        }

        var culprits = await page.EvaluateAsync<string[]>(OverflowProbe);

        overflow.ShouldBeLessThanOrEqualTo(
            0,
            $"{screen} overflows horizontally at 375px width ({overflow}px). " +
            $"Widest elements past the edge: {string.Join(" | ", culprits)}");
    }

    /// <summary>
    /// Writes one row into every list the 375px walk visits.
    /// </summary>
    /// <remarks>
    /// Through the management API rather than through the console: the walk is
    /// measuring LAYOUT, and driving nine creation forms to get there would make
    /// it a test of those forms instead. The agent, tool, workflow and provider
    /// catalogues come from the host's own code definitions already.
    /// </remarks>
    internal static async Task SeedEveryListAsync(UiHost host)
    {
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        async Task SendAsync(Task<HttpResponseMessage> call, string path)
        {
            using var response = await call;

            response.IsSuccessStatusCode.ShouldBeTrue(
                $"Seeding {path} failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        Task WriteAsync(string path, object body) =>
            SendAsync(client.PutAsJsonAsync($"{host.Prefix}{path}", body), path);

        // 🚨 PUT on `/api/agents/{name}` UPDATES; creating one is a POST to the
        // collection. The same shape does not work for both.
        Task CreateAsync(string path, object body) =>
            SendAsync(client.PostAsJsonAsync($"{host.Prefix}{path}", body), path);

        // A long name and a long endpoint on purpose: a narrow screen overflows
        // on the widest cell it is given, not on the average one.
        await WriteAsync(
            "/api/skills/refund-policy-for-late-deliveries",
            new
            {
                name = "refund-policy-for-late-deliveries",
                description = "How to decide and word a refund when the order shipped late.",
                instructions = "Check the order age, then state the decision in one sentence.",
            });

        await WriteAsync(
            "/api/triggers/helpdesk-webhook",
            new
            {
                targetKind = "Agent",
                targetName = "support",
                signingSecretConfigurationName = "Tracon:TriggerSecrets:Helpdesk",
                enabled = true,
            });

        await WriteAsync(
            "/api/mcp-servers/knowledge-base",
            new
            {
                description = "Read-only company handbook.",
                endpoint = "https://mcp.example.invalid/a-deliberately-long-path/sse",
                transport = "Sse",
                enabled = true,
                requiresApproval = true,
            });

        await WriteAsync(
            "/api/schedules/nightly-unresolved-ticket-summary",
            new
            {
                handlerKey = "tracon.agent-batch",
                targetName = "support",
                cron = "0 3 * * *",
                timeZone = "UTC",
                payload = new[] { "Summarise yesterday's unresolved tickets." },
                enabled = true,
            });

        await WriteAsync(
            "/api/evals/customer-support-regression-suite",
            new
            {
                agentName = "support",
                description = "Answers that must not regress.",
                checks = new[] { new { kind = "nonEmpty", minLength = 10 } },
            });

        // 🚨 An experiment needs a STORED agent: a code-defined one has no version
        // history, so there is nothing to split traffic between. The server says
        // so with a 400, which is how this seeding found out.
        await CreateAsync(
            "/api/agents",
            new
            {
                name = "stored-support-assistant",
                displayName = "Stored support assistant",
                description = "A database-defined agent, so an experiment has versions to split.",
                instructions = "Answer the question in one sentence.",
                model = new { provider = ScriptedModels.ProviderName, model = ScriptedModels.Default },
            });

        await WriteAsync(
            "/api/experiments/support-instruction-rewrite",
            new
            {
                agentName = "stored-support-assistant",
                variants = new[]
                {
                    new { name = "control", version = 1, weight = 50 },
                    new { name = "candidate", version = 1, weight = 50 },
                },
            });

        // The audit trail is written as a SIDE EFFECT of the writes above, so it
        // has rows by now without this test creating one directly.
    }

    /// <summary>
    /// The sentence the stubbed session store fails with, exactly as the console
    /// renders it — `TraconError` joins problem+json's title and detail.
    /// </summary>
    internal const string ServerFailureText =
        "Session store unavailable: The session store did not answer.";

    /// <summary>
    /// Names the elements whose right edge is past the viewport, innermost
    /// first, so a failure says WHAT is too wide instead of only by how much.
    /// </summary>
    internal const string OverflowProbe = """
        () => {
          const edge = document.documentElement.clientWidth;

          return [...document.querySelectorAll('*')]
            .map((element) => ({ element, box: element.getBoundingClientRect() }))
            .filter((entry) => entry.box.right > edge + 1 && entry.box.width > 0)
            .sort((left, right) => right.box.right - left.box.right)
            .slice(0, 5)
            .map(
              (entry) =>
                `${entry.element.tagName.toLowerCase()}.${[...entry.element.classList].slice(0, 4).join('.')}` +
                ` right=${Math.round(entry.box.right)} width=${Math.round(entry.box.width)}`,
            );
        }
        """;
}
