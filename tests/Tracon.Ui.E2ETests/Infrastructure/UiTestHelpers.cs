using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// The helpers more than one console test class uses.
/// </summary>
/// <remarks>
/// <c>UiTests.cs</c> was split into one class per screen. These members
/// moved here unchanged except for their visibility; a class reaches them
/// through <c>using static</c>, so its test bodies read exactly as before.
/// </remarks>
internal static class UiTestHelpers
{
    /// <summary>Pattern that matches the run link on the Playground screen.</summary>
    internal static readonly Regex RunLinkPattern =
        new("^run ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Waits until the agent editor has chosen the scripted provider, so its form is valid to save.</summary>
    internal static async Task WaitForDefaultProviderAsync(IPage page)
    {
        // The editor renders before its provider catalogue query completes.
        // Filling the other fields proves neither that the query settled nor
        // that the form is valid; under CI load the Save button can therefore
        // remain disabled until Playwright's unrelated click timeout expires.
        await Expect(page.GetByTestId("agent-provider"))
            .ToHaveValueAsync(ScriptedModels.ProviderName, new() { Timeout = 60_000 });
    }

    /// <summary>
    /// The page state that means "the agent <paramref name="name"/> is saved and
    /// its stored version is <paramref name="version"/>": the agent's detail
    /// screen, which shows the version the server answered with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 The heading alone is not a saved signal. The editor's own title is
    /// "Edit {name}", and a role locator's <c>Name</c> matches as a
    /// case-insensitive SUBSTRING by default: a wait for a heading named
    /// <c>{name}</c> is satisfied on the edit page while the save request is
    /// still in flight. A test that moved on from there (the experiment
    /// screen, a reload) could abandon the save and read the old version
    /// list. The detail screen is reached only after the save answered
    /// (<c>navigate</c> in the editor's <c>onSuccess</c>), so its exact heading
    /// plus the stored version in the origin badge cannot hold on the editor.
    /// </para>
    /// <para>
    /// It is one locator so that a test can read its count at any moment
    /// without waiting. <c>Has</c> is queried from the outer match, so both
    /// parts must be on the page at once.
    /// </para>
    /// </remarks>
    internal static ILocator AgentSavedMarker(IPage page, string name, int version) =>
        page.Locator("body")
            .Filter(new()
            {
                Has = page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true }),
            })
            .Filter(new()
            {
                Has = page.GetByText($"db · v{version}", new() { Exact = true }),
            });

    /// <summary>
    /// Waits until the agent is saved with <paramref name="version"/> as its
    /// stored version; see <see cref="AgentSavedMarker"/> for why a heading is
    /// not enough.
    /// </summary>
    internal static async Task WaitForAgentSavedAsync(IPage page, string name, int version)
    {
        await Expect(AgentSavedMarker(page, name, version)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Creates a database agent from the UI and waits until it is saved as
    /// version 1.
    /// </summary>
    internal static async Task CreateAgentAsync(
        UiHost host,
        Session session,
        string name,
        string instructions)
    {
        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await WaitForDefaultProviderAsync(session.Page);
        await session.Page.GetByTestId("agent-name").FillAsync(name);
        await session.Page.GetByTestId("agent-instructions").FillAsync(instructions);
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        var save = session.Page.GetByTestId("agent-save");
        await Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();

        await WaitForAgentSavedAsync(session.Page, name, 1);
    }

    /// <summary>
    /// From the agent's detail screen, opens the editor and replaces the
    /// instructions, leaving Save enabled and unclicked.
    /// </summary>
    internal static async Task EditInstructionsAsync(Session session, string instructions)
    {
        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync();
        await session.Page.GetByTestId("agent-instructions").FillAsync(instructions);
        await Expect(session.Page.GetByTestId("agent-save")).ToBeEnabledAsync();
    }

    /// <summary>
    /// Creates a database agent from the UI, then edits its instructions to
    /// produce a second version. Shared precondition for the diff and
    /// experiment tests.
    /// </summary>
    internal static async Task CreateAgentWithTwoVersionsAsync(
        UiHost host,
        Session session,
        string name,
        string firstInstructions,
        string secondInstructions)
    {
        await CreateAgentAsync(host, session, name, firstInstructions);

        await EditInstructionsAsync(session, secondInstructions);
        await session.Page.GetByTestId("agent-save").ClickAsync();

        await WaitForAgentSavedAsync(session.Page, name, 2);
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
