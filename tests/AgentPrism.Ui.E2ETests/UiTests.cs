using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPrism.Ui.E2ETests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace AgentPrism.Ui.E2ETests;

/// <summary>
/// Verifies the embedded UI in a real browser.
/// </summary>
/// <remarks>
/// Each test sets up its own server and its own browser context; there is no
/// shared state between tests. Stores are in-memory, so setup cost is low.
/// </remarks>
public sealed class UiTests(BrowserFixture browsers)
{
    /// <summary>Pattern that matches the run link on the Playground screen.</summary>
    private static readonly Regex RunLinkPattern =
        new("^run ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Pattern that matches the job detail heading.</summary>
    private static readonly Regex JobHeadingPattern =
        new("^Job ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Pattern that matches the eval run detail heading.</summary>
    private static readonly Regex EvalRunHeadingPattern =
        new("^Eval run ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Pattern that matches the "N run(s)" button on the session page.</summary>
    private static readonly Regex SessionRunsButtonPattern =
        new(@"^\d+ runs?$", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Ui_opens_and_home_screen_renders()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        // Dashboard is the landing screen (Phase 20); Agents stays first in the side menu.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        (await session.Page.TitleAsync()).ShouldBe("AgentPrism");

        // All management screens must be in the navigation bar.
        foreach (var screen in new[] { "Agents", "Dashboard", "Playground", "Sessions", "Workflows", "Jobs", "Evals", "Runs", "Tools", "Skills", "Models", "MCP", "Approvals", "Settings" })
        {
            (await session.Page.GetByRole(AriaRole.Link, new() { Name = screen }).CountAsync())
                .ShouldBeGreaterThan(0, $"Link '{screen}' was not found.");
        }
    }

    [Fact]
    public async Task Dashboard_charts_render_and_range_can_be_changed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        // Buckets are filled with zeros even for an empty store; charts still render.
        await session.Page.GetByTestId("timeseries-chart").WaitForAsync();
        await session.Page.GetByTestId("status-distribution-chart").WaitForAsync();

        // 🚨 `.First` is REQUIRED: the empty-state text is shared by every chart
        // panel, and an empty dashboard now shows it in two of them (the model
        // breakdown and, since phase 68, the token breakdown). A bare
        // GetByText resolves to both and fails Playwright's strict mode.
        await session.Page.GetByText("No run in this window").First.WaitForAsync();

        // Changing the range must trigger a new /api/stats/timeseries request
        // (the 30d range switches from an hour bucket to a day bucket, to stay
        // under the 500-bucket limit).
        var timeseriesRefetched = session.Page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/stats/timeseries", StringComparison.Ordinal) &&
            response.Url.Contains("bucket=Day", StringComparison.Ordinal));

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "30d" }).ClickAsync();

        await timeseriesRefetched;

        await session.Page.GetByTestId("timeseries-chart").WaitForAsync();
    }

    [Fact]
    public async Task General_screens_at_375px_width_do_not_overflow_horizontally()
    {
        // BUG-S4-008. The third root cause on the Tools screen (the badge row
        // not wrapping when a tool is used by more than one agent) cannot be
        // triggered with this fixed data set (each tool belongs to a single
        // agent); that branch is verified separately against a live server
        // (KAPANIS-PLANI.md §6).
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.SetViewportSizeAsync(375, 812);

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Dashboard");

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();
        await AssertNoHorizontalOverflowAsync(session.Page, "Settings");
    }

    [Fact]
    public async Task Code_defined_agent_appears_in_list_and_cannot_be_edited()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents");

        await session.Page.GetByText("Support assistant").WaitForAsync();

        await session.Page.GetByText("Support assistant").ClickAsync();

        // Write endpoints return 409 for a code-defined agent; the UI must
        // never show the edit button.
        await session.Page.GetByText("This agent is declared in code").WaitForAsync();

        (await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Navigating_directly_to_code_agent_edit_URL_fills_form_with_name()
    {
        // BUG-S4-010: the list screen hides "Edit" (per task K1), but
        // navigating directly to the edit URL opened the form empty with a
        // read-only "Name" field — the Validate/Save buttons could never be
        // enabled, so the expected 409 flow for the case was unreachable.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/support/edit");

        var name = session.Page.GetByTestId("agent-name");

        await Assertions.Expect(name).ToHaveValueAsync("support", new() { Timeout = 10_000 });
        (await name.IsEditableAsync()).ShouldBeFalse("The code agent's name must still not be editable.");

        (await session.Page.GetByTestId("agent-validate").IsDisabledAsync())
            .ShouldBeFalse("The Validate button stayed disabled even though the form is filled.");
        (await session.Page.GetByTestId("agent-save").IsDisabledAsync())
            .ShouldBeFalse("The Save button stayed disabled even though the form is filled.");
    }

    [Fact]
    public async Task Shell_CSP_header_allowlists_blob_URLs_for_attachment_preview_and_speech()
    {
        // No browser needed: the root cause of BUG-S4-011 is a single
        // constant string (`EmbeddedUiProvider.ContentSecurityPolicy`). The
        // two browser tests below
        // (`Playground_response_can_be_spoken_and_audio_element_plays`,
        // `Playground_file_upload_shows_preview_and_run_continues`) verify
        // the same defect via the CSP violation event; this test checks that
        // value directly, in seconds.
        await using var host = await UiHost.StartAsync();

        using var http = new HttpClient();
        using var response = await http.GetAsync(host.UiAddress);

        response.Headers.TryGetValues("Content-Security-Policy", out var values).ShouldBeTrue();
        var csp = values!.Single();

        // Both the attachment preview (useAttachmentPreview) and the "Speak"
        // playback (SpeakButton) wrap fetched bytes with URL.createObjectURL
        // instead of a direct endpoint that cannot carry a bearer token; the
        // source is always a blob: URL.
        csp.ShouldContain("img-src 'self' data: blob:");
        csp.ShouldContain("media-src 'self' blob:");
    }

    [Fact]
    public async Task Playground_response_can_be_spoken_and_audio_element_plays()
    {
        // 🚨 Token is ON. This is the whole point of this test: if
        // `<audio src="api/attachments/{id}">` were written, the browser
        // would NOT attach the bearer header to the resource load and the
        // request would get a 401. The UI must fetch the bytes and wrap them
        // in an object URL.
        const string token = "e2e-audio-token";

        await using var host = await UiHost.StartAsync(authToken: token);
        await using var session = await Session.OpenAsync(browsers, host);

        // The token is entered on the shell screen; after that it is a normal session.
        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByLabel("Token").FillAsync(token);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        // BUG-S4-011: the `media-src` directive did not exist at all,
        // `<audio src="blob:...">` was silently rejected (`audio.error.code=4`).
        // The presence of the `src` attribute does NOT catch this; listen for
        // the CSP violation event to prove the browser actually allowed the
        // resource to open.
        await session.Page.EvaluateAsync(
            """
            () => {
                window.__cspViolations = [];
                document.addEventListener('securitypolicyviolation', (event) => {
                    window.__cspViolations.push(event.violatedDirective);
                });
            }
            """);

        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        var speak = session.Page.GetByTestId("playground-speak").First;
        await speak.WaitForAsync(new() { Timeout = 20_000 });
        await speak.ClickAsync();

        var audio = session.Page.GetByTestId("playground-audio").First;
        await audio.WaitForAsync(new() { Timeout = 20_000 });

        // The source is an object URL; it must NOT be the endpoint address.
        var source = await audio.GetAttributeAsync("src");
        source.ShouldNotBeNull();
        source.StartsWith("blob:", StringComparison.Ordinal).ShouldBeTrue(source);

        var hadCspViolation = await session.Page.EvaluateAsync<bool>(
            """
            () => new Promise((resolve) => {
                if (window.__cspViolations.length > 0) { resolve(true); return; }
                setTimeout(() => resolve(window.__cspViolations.length > 0), 500);
            })
            """);
        hadCspViolation.ShouldBeFalse();
    }

    [Fact]
    public async Task Playground_voice_mode_opens_microphone_and_shows_transcript()
    {
        // 🚨 Runs with a fake media device (BrowserFixture flags). There is no
        // real microphone; Chromium generates a fixed tone and the client's
        // VAD counts it as speech.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        await session.Page.GetByTestId("voice-mode").ClickAsync();
        await session.Page.GetByTestId("voice-toggle").ClickAsync();

        // Once the handshake completes the server sends `ready` and the panel starts listening.
        await session.Page.GetByTestId("voice-meter").WaitForAsync(new() { Timeout = 20_000 });

        // 🚨 Silence detection CANNOT be used here: Chromium's fake device
        // produces a continuous tone and never goes quiet. The manual commit
        // button is already a real need (noisy environment, push-to-talk) and
        // the test uses it.
        var commit = session.Page.GetByTestId("voice-commit");
        await commit.WaitForAsync(new() { Timeout = 20_000 });
        await commit.ClickAsync();

        var transcript = session.Page.GetByTestId("voice-transcript");
        await transcript.WaitForAsync(new() { Timeout = 30_000 });

        // The resolved text comes from the server; the fake provider returns a fixed response.
        await transcript.GetByText("where is my order").First.WaitForAsync(new() { Timeout = 30_000 });

        // Audio is NOT stored by default; the recording notice must not appear.
        (await session.Page.GetByTestId("voice-recording-notice").CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Playground_stream_arrives_and_tool_card_fills_in()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        // The tool card is created when a real FunctionCallContent arrives.
        var card = session.Page.GetByTestId("tool-card").First;

        await card.WaitForAsync(new() { Timeout = 20_000 });
        await card.GetByText("get_order_status").WaitForAsync();
        await card.GetByText("done").WaitForAsync(new() { Timeout = 20_000 });

        // The model response streams in after the tool result.
        await session.Page.GetByText("Echo: where is ORD-7").WaitForAsync(new() { Timeout = 20_000 });

        // Bridge to the run record: the first SSE frame carries the run id.
        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Playground_file_upload_shows_preview_and_run_continues()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var pngPath = Path.Combine(Path.GetTempPath(), $"agentprism-e2e-{Guid.NewGuid():N}.png");

        // 8-byte PNG signature plus a little padding: a valid signature is
        // enough to pass the magic-byte check, a full PNG body is not needed.
        await File.WriteAllBytesAsync(
            pngPath,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        try
        {
            await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

            // BUG-S4-011: `img-src` did not carry blob:, so the thumbnail
            // preview (`useAttachmentPreview`) silently disappeared
            // (`img.naturalWidth=0`). The chip being visible does NOT catch
            // this; verify with the CSP violation event that the browser
            // really allowed the object URL to load.
            await session.Page.EvaluateAsync(
                """
                () => {
                    window.__cspViolations = [];
                    document.addEventListener('securitypolicyviolation', (event) => {
                        window.__cspViolations.push(event.violatedDirective);
                    });
                }
                """);

            await session.Page.GetByTestId("attachment-input").SetInputFilesAsync(pngPath);

            var chip = session.Page.GetByTestId("attachment-chip").First;
            await chip.WaitForAsync(new() { Timeout = 10_000 });
            await chip.GetByText(Path.GetFileName(pngPath)).WaitForAsync();

            var hadCspViolation = await session.Page.EvaluateAsync<bool>(
                """
                () => new Promise((resolve) => {
                    if (window.__cspViolations.length > 0) { resolve(true); return; }
                    setTimeout(() => resolve(window.__cspViolations.length > 0), 500);
                })
                """);
            hadCspViolation.ShouldBeFalse();

            await session.Page.GetByTestId("playground-input").FillAsync("describe this image");
            await session.Page.GetByTestId("playground-send").ClickAsync();

            // The attachment reference also stays in the turn as a small submission preview.
            await session.Page.GetByTestId("attachment-chip").First.WaitForAsync(new() { Timeout = 20_000 });
            await session.Page.GetByText("Echo: describe this image").WaitForAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            File.Delete(pngPath);
        }
    }

    [Fact]
    public async Task Agent_created_from_UI_can_be_run_immediately()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("ui-agent");
        await session.Page.GetByTestId("agent-display-name").FillAsync("Console agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        // After saving, the detail screen opens and the new definition appears.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Console agent" })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/playground/ui-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo: hello").WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Fallback_list_is_saved_and_read_back()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("fallback-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        await session.Page.GetByTestId("add-fallback").ClickAsync();
        await session.Page.GetByTestId("fallback-provider-0").SelectOptionAsync(ScriptedModels.ProviderName);
        await session.Page.GetByTestId("fallback-model-0").FillAsync(ScriptedModels.Support);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "fallback-agent" })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        (await session.Page.GetByTestId("fallback-provider-0").InputValueAsync()).ShouldBe(ScriptedModels.ProviderName);
        (await session.Page.GetByTestId("fallback-model-0").InputValueAsync()).ShouldBe(ScriptedModels.Support);

        // Removing the only row returns to the empty-list hint, and a save
        // round trip persists the now-empty list (K1: no lingering fallback).
        await session.Page.GetByTestId("remove-fallback-0").ClickAsync();
        (await session.Page.GetByTestId("fallback-provider-0").CountAsync()).ShouldBe(0);

        await session.Page.GetByTestId("agent-save").ClickAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "fallback-agent" })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        (await session.Page.GetByTestId("fallback-provider-0").CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Context_panel_reflects_selected_strategy_in_request_preview()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("context-agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);

        // Conditional fields (trigger, etc.) stay hidden until a strategy is selected.
        (await session.Page.GetByLabel("Trigger: message count").CountAsync()).ShouldBe(0);

        await session.Page.GetByLabel("Compaction strategy").SelectOptionAsync("SlidingWindow");
        await session.Page.GetByLabel("Trigger: message count").FillAsync("40");
        await session.Page.GetByLabel("Enable todo tracking").CheckAsync();

        var preview = await session.Page.Locator("pre").First.TextContentAsync();

        preview.ShouldNotBeNull();
        preview.ShouldContain("\"strategy\": \"SlidingWindow\"");
        preview.ShouldContain("\"triggerMessages\": 40");
        preview.ShouldContain("\"enableTodo\": true");
    }

    [Fact]
    public async Task Skill_created_from_UI_is_listed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/skills/new");

        await session.Page.GetByPlaceholder("invoice-analysis").FillAsync("invoice-review");
        await session.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description" })
            .FillAsync("Reviews invoices.");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Skills" }).WaitForAsync();
        await session.Page.GetByText("invoice-review", new() { Exact = true }).WaitForAsync();
    }

    [Fact]
    public async Task Trigger_created_from_UI_is_listed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/triggers/new");

        await session.Page.GetByRole(AriaRole.Textbox, new() { Name = "Name *", Exact = true })
            .FillAsync("slack-e2e");
        await session.Page.GetByPlaceholder("demo").FillAsync("support");
        await session.Page.GetByPlaceholder("AgentPrism:TriggerSecrets:Slack")
            .FillAsync("AgentPrism:TriggerSecrets:SlackE2E");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Triggers" }).WaitForAsync();
        await session.Page.GetByText("slack-e2e", new() { Exact = true }).WaitForAsync();
    }

    [Fact]
    public async Task Assets_load_under_a_different_prefix()
    {
        await using var host = await UiHost.StartAsync(prefix: "/panel");
        await using var session = await Session.OpenAsync(browsers, host);

        var failures = new List<string>();

        session.Page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                failures.Add($"{response.Status} {response.Url}");
            }
        };

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        failures.ShouldBeEmpty();

        // A deep route must also load the shell; the client performs the redirect.
        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();

        await session.Page.GetByText("/panel", new() { Exact = false }).First.WaitForAsync();
    }

    [Fact]
    public async Task Dark_theme_toggle_works_and_persists()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        var before = await session.Page.GetAttributeAsync("html", "data-theme");

        await session.Page.GetByTestId("theme-toggle").ClickAsync();

        var after = await session.Page.GetAttributeAsync("html", "data-theme");

        string.Equals(after, before, StringComparison.Ordinal)
            .ShouldBeFalse("The theme did not change.");
        (after is "light" or "dark").ShouldBeTrue($"Unexpected theme: {after}");

        // The preference lives in localStorage; a reload must preserve it.
        await session.Page.ReloadAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        string.Equals(await session.Page.GetAttributeAsync("html", "data-theme"), after, StringComparison.Ordinal)
            .ShouldBeTrue("The theme preference was not preserved across reload.");
    }

    [Fact]
    public async Task Theme_selector_in_settings_updates_top_bar_toggle_in_same_session()
    {
        // BUG-S4-006: the <select> on the Settings screen applied the theme
        // correctly (<html data-theme> really changed), but the top bar's
        // ThemeToggle never learned about its own separate useState — it kept
        // showing the wrong icon/title (title="Theme: dark") for the rest of
        // the SPA session, until a full page reload.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();

        // First switch to "dark" via the top bar toggle: the "light" selection
        // below is then a real change, independent of this machine's system
        // color preference.
        if (!string.Equals(await session.Page.GetAttributeAsync("html", "data-theme"), "dark", StringComparison.Ordinal))
        {
            await session.Page.GetByTestId("theme-toggle").ClickAsync();
        }

        (await session.Page.GetAttributeAsync("html", "data-theme")).ShouldBe("dark");

        // The wrapping <label>'s accessible name concatenates the <select>'s
        // own rendered option text ("ThemeFollow systemLightDark"), so
        // GetByLabel("Theme") never matches exactly; scope by the wrapper
        // instead.
        await session.Page.Locator("label", new() { HasText = "Theme" }).Locator("select")
            .SelectOptionAsync("light");

        (await session.Page.GetAttributeAsync("html", "data-theme")).ShouldBe("light");

        (await session.Page.GetByTestId("theme-toggle").GetAttributeAsync("title"))
            .ShouldBe("Theme: light", "The top bar toggle did not learn about the change made in Settings.");
    }

    [Fact]
    public async Task Shell_opens_and_asks_for_token_when_required()
    {
        await using var host = await UiHost.StartAsync(authToken: "secret-token");
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        // The shell is exempt from the bearer token layer; otherwise the user
        // could never see the screen where the token is entered.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Access token required" })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByLabel("Token").FillAsync("secret-token");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Run_events_appear_step_by_step_on_screen()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo: where is ORD-7").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        // Event names are a stable contract; the screen shows them as-is.
        foreach (var name in new[] { "run.started", "tool.invoking", "tool.invoked", "run.completed" })
        {
            await session.Page.GetByText(name, new() { Exact = true }).First
                .WaitForAsync(new() { Timeout = 20_000 });
        }
    }

    [Fact]
    public async Task Child_agent_run_appears_as_a_tree()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/router");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        // Summary events appear in the root stream; the child run's full
        // stream is not mirrored (event volume would multiply across the tree).
        await session.Page.GetByText("child.started", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });

        // The tree panel must show both the root and the child run.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Call tree" })
            .WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GetByText("this run", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Child_run_trace_panel_shows_go_to_root_link_and_does_not_hang_loading()
    {
        // BUG-S4-013: once the trace query fell outside `enabled: finished &&
        // parentRunId == null` (true for every child run), React Query's
        // `isPending` stayed PERMANENTLY true; because the panel checked this
        // FIRST, it showed an endless "Loading" and the "root run" link never
        // appeared.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/router");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        var callTree = session.Page.Locator(
            "section", new() { Has = session.Page.GetByRole(AriaRole.Heading, new() { Name = "Call tree" }) });

        await callTree.WaitForAsync(new() { Timeout = 30_000 });

        // The only link in the tree row is the child run (the root row itself
        // - "this run" - is plain text, not a link).
        await callTree.Locator("a[href*='/runs/']").First.ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Trace" }).WaitForAsync();

        await session.Page.GetByText("Spans live on the root run").WaitForAsync(new() { Timeout = 15_000 });
        await session.Page.GetByRole(AriaRole.Link, new() { Name = "root run" }).WaitForAsync();
    }

    [Fact]
    public async Task Runs_screen_lists_only_roots_by_default()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/router");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // The default view lists only roots and reports the child run count
        // with a badge.
        await session.Page.GetByText("1 child run", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });

        (await session.Page.GetByText("depth 1", new() { Exact = true }).CountAsync()).ShouldBe(0);

        await session.Page.GetByRole(AriaRole.Combobox).Last.SelectOptionAsync("all");

        await session.Page.GetByText("depth 1", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Runs_button_on_session_page_navigates_to_filtered_list()
    {
        // BUG-S4-017: navigate() carried the query string (?sessionId=...)
        // into path matching; clicking inside the SPA produced "Page not
        // found", only a full page reload (window.location.pathname naturally
        // drops the query) worked.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-7");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.Locator("a[href*='/sessions/']").First.ClickAsync();

        var runsButton = session.Page.GetByRole(AriaRole.Button, new() { NameRegex = SessionRunsButtonPattern });

        await runsButton.WaitForAsync(new() { Timeout = 15_000 });

        var label = await runsButton.TextContentAsync();
        var expectedCount = int.Parse(label!.Split(' ')[0], CultureInfo.InvariantCulture);

        await runsButton.ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Runs" })
            .WaitForAsync(new() { Timeout = 15_000 });
        (await session.Page.GetByText("Page not found").CountAsync()).ShouldBe(0);

        (await session.Page.Locator("tbody tr").CountAsync()).ShouldBe(expectedCount);
    }

    [Fact]
    public async Task Mcp_screen_states_security_boundary_and_server_can_be_added()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/mcp");

        // The security boundary must be STATED on screen: adding an MCP
        // server means accepting tool definitions from an external source.
        await session.Page.GetByText("Security boundary").WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add server" }).ClickAsync();

        // The placeholder "github" also matches "AgentPrism:Mcp:GithubToken";
        // an exact match must be requested.
        await session.Page.GetByPlaceholder("github", new() { Exact = true }).FillAsync("sample");
        await session.Page.GetByPlaceholder("https://mcp.example.com/mcp")
            .FillAsync("https://mcp.sample.test/mcp");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByText("sample").First.WaitForAsync(new() { Timeout = 10_000 });

        // The approval badge must appear: MCP tools require approval by default.
        await session.Page.GetByText("approval", new() { Exact = true }).First.WaitForAsync();
    }

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

        await session.Page.GetByText("refund_order").First.WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("amount ≤ 100").WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Models_screen_shows_health_badge()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/models");

        // FakeModelProvider does not implement IModelProviderHealthCheck; this
        // is not a bug and the badge must show "unknown". The leftover
        // "provider connectivity checks are still missing" note from Phase 6
        // must no longer appear on screen.
        await session.Page.GetByText("unknown", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });

        (await session.Page.GetByText("Provider connectivity checks are still missing").CountAsync()).ShouldBe(0);

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Check now" }).First.ClickAsync();

        await session.Page.GetByText("unknown", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Tools_screen_shows_call_count()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        // First run a turn that produces a real tool call.
        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-9");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo: where is ORD-9").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/tools");

        // In Phase 5 this screen carried a "arrives in the observability
        // phase" note (deviation S5); real counts must be shown now.
        await session.Page.GetByText("calls").First.WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Audit_screen_lists_audit_records()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using (var created = await client.PostAsJsonAsync(
            $"{host.Prefix}/api/agents",
            new
            {
                name = "audit-e2e",
                instructions = "test",
                model = new { provider = ScriptedModels.ProviderName, model = ScriptedModels.Default },
                toolNames = Array.Empty<string>(),
            }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/audit");

        await session.Page.GetByText("agent.create").First.WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("agent:audit-e2e").First.WaitForAsync();
    }

    [Fact]
    public async Task Write_buttons_are_hidden_for_reader_role()
    {
        // Phase 9: when the Admin policy fails, the UI must hide write
        // buttons — server-side authorization is still the only real
        // enforcement, this only prevents a bad experience (show it, get a 403).
        await using var host = await UiHost.StartAsync(
            configureServices: services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, policy => policy.RequireAssertion(_ => false)));

        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        (await session.Page.GetByRole(AriaRole.Link, new() { Name = "New agent" }).CountAsync()).ShouldBe(0);

        // Because the Admin role is not satisfied, the Audit tab must also not
        // appear in the navigation bar.
        (await session.Page.GetByRole(AriaRole.Link, new() { Name = "Audit" }).CountAsync()).ShouldBe(0);

        await session.Page.GotoAsync($"{host.UiAddress}/mcp");
        (await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add server" }).CountAsync()).ShouldBe(0);
    }

    /// <summary>The browser context for a single test.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly IBrowserContext _context;

        private Session(IBrowserContext context, IPage page)
        {
            _context = context;
            Page = page;
        }

        public IPage Page { get; }

        /// <param name="locale">
        /// The browser's language.
        /// <para>
        /// 🚨 Since Phase 30 the UI picks its own language from
        /// <c>navigator.language</c>. A test that asserts on text MUST pin the
        /// language; otherwise the test's outcome depends on the running
        /// machine's system language. The default is therefore <c>en-US</c>,
        /// and only the language tests pass a different value.
        /// </para>
        /// </param>
        public static async Task<Session> OpenAsync(
            BrowserFixture browsers,
            UiHost host,
            string locale = "en-US")
        {
            var context = await browsers.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                Locale = locale,
            });

            return new Session(context, await context.NewPageAsync());
        }

        public async ValueTask DisposeAsync() => await _context.CloseAsync();
    }
    // --- Phase 16: workflow graph and human-in-the-loop ---

    [Fact]
    public async Task Workflow_graph_renders()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows");

        await session.Page.GetByText("summarize-and-translate").WaitForAsync();
        await session.Page.GetByText("summarize-and-translate").ClickAsync();

        var graph = session.Page.GetByTestId("workflow-graph");

        await graph.WaitForAsync();

        // The built-in pattern adds an output node that the user did not
        // write: two agents + OutputMessages. The graph is rendered from the
        // COMPILED workflow, not the definition, so that node also appears.
        var nodes = session.Page.GetByTestId("workflow-node");

        (await nodes.CountAsync()).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Mermaid_text_can_be_copied()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await session.Page.GotoAsync($"{host.UiAddress}/workflows/summarize-and-translate");

        await session.Page.GetByTestId("workflow-graph").WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).First.ClickAsync();

        var copied = await session.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // The copied text is produced by Microsoft Agent Framework; the UI
        // does not render it, it only passes it through (bundle budget, K-002).
        copied.ShouldContain("flowchart", Case.Sensitive);
    }

    [Fact]
    public async Task Pending_request_card_can_be_answered()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");

        await session.Page.GetByTestId("workflow-graph").WaitForAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        // The run pauses waiting for a human response; the card appears then.
        var card = session.Page.GetByTestId("workflow-pending-request");

        await card.WaitForAsync(new() { Timeout = 20_000 });

        (await card.InnerTextAsync()).ShouldContain("publish the report", Case.Sensitive);

        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        // The response opens a NEW run and produces the graph output.
        var output = session.Page.GetByTestId("workflow-output");

        await output.WaitForAsync(new() { Timeout = 20_000 });

        (await output.InnerTextAsync()).ShouldContain("approved", Case.Sensitive);
    }

    [Fact]
    public async Task Pending_run_appears_with_distinct_status_in_Runs_list()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/approval-flow");
        await session.Page.GetByTestId("workflow-graph").WaitForAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("publish the report");
        await session.Page.GetByTestId("workflow-run").ClickAsync();
        await session.Page.GetByTestId("workflow-pending-request").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Neither completed nor failed: it has its own status.
        //
        // 🚨 Exact is required: the status FILTER also has a hidden
        // <option>Awaiting input</option>, and a substring match would find
        // that first and wait for it to be visible. The badge is lowercase,
        // the option is capitalized; Exact tells them apart.
        await session.Page
            .GetByText("awaiting input", new() { Exact = true })
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    // --- Phase 17: batch and scheduled runs ---

    [Fact]
    public async Task Schedule_is_created_triggered_and_job_completes()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: services => services.UseScheduling(options =>
            {
                // The test does not need to wait in real time; the worker polls immediately.
                options.PollInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseDuration = TimeSpan.FromSeconds(10);
            }));
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/jobs");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Jobs", Exact = true }).WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "New schedule" }).ClickAsync();

        await session.Page.GetByPlaceholder("nightly-report").FillAsync("e2e-batch-job");
        await session.Page.GetByPlaceholder("summarizer").FillAsync("support");
        await session.Page.Locator("textarea").FillAsync("[\"hello\"]");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByText("e2e-batch-job").WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Trigger" }).ClickAsync();

        // The worker leases and runs the job on its fast poll interval; the
        // completion badge appears in the "Recent jobs" panel.
        await session.Page.GetByText("completed", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 15_000 });

        // Navigate to the job detail: the item input and run link appear.
        // "Recent jobs" is the SECOND table on the page (Schedules comes
        // first); the first row's link goes to the job id.
        await session.Page.Locator("table").Last.Locator("tbody tr").First
            .GetByRole(AriaRole.Link).First.ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { NameRegex = JobHeadingPattern })
            .WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("hello").WaitForAsync();
    }

    // --- Phase 18: evaluation (eval) ---

    [Fact]
    public async Task Eval_suite_is_created_case_added_and_run_passes()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: services => services.UseScheduling(options =>
            {
                // The test does not need to wait in real time; the worker polls immediately.
                options.PollInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseDuration = TimeSpan.FromSeconds(10);
            }));
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/evals");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Evals", Exact = true }).WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "New suite" }).ClickAsync();

        await session.Page.GetByPlaceholder("customer-support-suite").FillAsync("e2e-eval-suite");
        // The "support" scripted model runs without a tool and reflects the
        // input verbatim as "Echo: {query}" (FakeModelProvider) — so the
        // default nonEmpty check reliably passes.
        await session.Page.GetByPlaceholder("customer-support-agent").FillAsync("support");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "e2e-eval-suite" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "e2e-eval-suite", Exact = true })
            .WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add case" }).ClickAsync();
        await session.Page.GetByPlaceholder("What is your return policy?")
            .FillAsync("Where is my order, can you help?");

        var casesSaved = session.Page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/evals/e2e-eval-suite/cases", StringComparison.Ordinal) &&
            string.Equals(response.Request.Method, "PUT", StringComparison.Ordinal));
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save cases" }).ClickAsync();
        await casesSaved;

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Run now" }).ClickAsync();

        // The worker leases and runs the job on its fast poll interval; the
        // completion badge appears in the "Runs" panel.
        await session.Page.GetByText("completed", new() { Exact = true })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.Locator("table").Last.Locator("tbody tr").First
            .GetByRole(AriaRole.Link).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { NameRegex = EvalRunHeadingPattern })
            .WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("passed", new() { Exact = true }).WaitForAsync();
    }

    // --- Version comparison and A/B experiments (Phase 19) ---

    [Fact]
    public async Task Version_diff_compares_two_versions()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await CreateAgentWithTwoVersionsAsync(host, session, "diff-agent", "first instructions", "second instructions");

        await session.Page.GotoAsync($"{host.UiAddress}/agents/diff-agent");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "diff-agent" }).WaitForAsync();

        await session.Page.GetByTestId("version-checkbox-1").CheckAsync();
        await session.Page.GetByTestId("version-checkbox-2").CheckAsync();

        // When two versions are selected, both raw definitions are fetched and
        // a line-based diff is rendered; both instruction texts (one as "-",
        // one as "+") must appear.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Comparing v1 → v2" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // "first instructions"/"second instructions" also appear in the
        // Instructions panel and the raw Definition JSON; diff lines are
        // distinguished by the span.break-all class.
        await session.Page.Locator("span.break-all", new() { HasText = "first instructions" }).First.WaitForAsync();
        await session.Page.Locator("span.break-all", new() { HasText = "second instructions" }).First.WaitForAsync();
    }

    [Fact]
    public async Task Experiment_is_created_started_and_traffic_reflects_in_results_table()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await CreateAgentWithTwoVersionsAsync(host, session, "exp-agent", "v1 instructions", "v2 instructions");

        await session.Page.GotoAsync($"{host.UiAddress}/experiments");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Experiments" }).First.WaitForAsync();

        await session.Page.GetByTestId("new-experiment").ClickAsync();
        await session.Page.GetByTestId("experiment-name").FillAsync("e2e-experiment");
        await session.Page.GetByTestId("experiment-agent-name").FillAsync("exp-agent");

        // The version dropdown fills from the agentVersions query once the agent name is entered.
        await session.Page.GetByTestId("variant-name-0").FillAsync("control");
        await session.Page.GetByTestId("variant-version-0").SelectOptionAsync("1");
        await session.Page.GetByTestId("variant-weight-0").FillAsync("50");

        await session.Page.GetByTestId("variant-name-1").FillAsync("v2");
        await session.Page.GetByTestId("variant-version-1").SelectOptionAsync("2");
        await session.Page.GetByTestId("variant-weight-1").FillAsync("50");

        await session.Page.GetByTestId("experiment-save").ClickAsync();

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "e2e-experiment" }).ClickAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "e2e-experiment" }).WaitForAsync();

        await session.Page.GetByTestId("experiment-start").ClickAsync();
        await session.Page.GetByText("running", new() { Exact = true }).WaitForAsync(new() { Timeout = 10_000 });

        // A request is sent to the agent while the experiment is running;
        // since assignment is deterministic, this run is written to one of
        // the two arms.
        await session.Page.GotoAsync($"{host.UiAddress}/playground/exp-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("hello");
        await session.Page.GetByTestId("playground-send").ClickAsync();
        await session.Page.GetByText("Echo: hello").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/experiments/e2e-experiment");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "e2e-experiment" }).WaitForAsync();

        // The results table shows raw counts; at least one column must show a
        // total of 1 run. There is no statistical "winner" claim anywhere.
        await session.Page.GetByRole(AriaRole.Cell, new() { Name = "1", Exact = true }).First
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByTestId("experiment-stop").ClickAsync();
        await session.Page.GetByText("stopped", new() { Exact = true }).WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Quota_is_added_and_usage_bar_appears()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add quota" }).ClickAsync();
        await session.Page.GetByTestId("quota-max-runs").FillAsync("100");
        await session.Page.GetByTestId("quota-save").ClickAsync();

        // Once the rule is saved, the row and the usage bar appear.
        await session.Page.GetByTestId("quota-row").First.WaitForAsync(new() { Timeout = 15_000 });
        await session.Page.GetByTestId("quota-bar").First.WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByText("0 / 100", new() { Exact = false }).First.WaitForAsync();
    }

    [Fact]
    public async Task Webhook_is_added_and_test_send_completes()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add webhook" }).ClickAsync();
        await session.Page.GetByTestId("webhook-name").FillAsync("order-service");
        await session.Page.GetByTestId("webhook-url").FillAsync("https://example.com/hooks/agentprism");

        // 🚨 What goes here is NOT a secret, it is the NAME of the key the
        // secret will be read from.
        await session.Page.GetByTestId("webhook-secret-key")
            .FillAsync("AgentPrism:Webhooks:Secrets:order-service");

        await session.Page.GetByTestId("webhook-save").ClickAsync();

        await session.Page.GetByTestId("webhook-row").First.WaitForAsync(new() { Timeout = 15_000 });

        // The key's name appears on screen; its value never does.
        await session.Page.GetByText("AgentPrism:Webhooks:Secrets:order-service", new() { Exact = false })
            .First.WaitForAsync();

        await session.Page.GetByTestId("webhook-test").First.ClickAsync();
        await session.Page.GetByTestId("webhook-test-result").WaitForAsync(new() { Timeout = 15_000 });
    }


    // --- Phase 30: localization, command palette, and shortcuts ---

    [Fact]
    public async Task Language_is_selected_from_browser_language()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        await session.Page.GotoAsync(host.UiAddress);

        // With no stored preference, the default language comes from navigator.language.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Gösterge Paneli" })
            .WaitForAsync(new() { Timeout = 15_000 });

        // 🚨 The lang attribute is not decoration: it is what the screen
        // reader picks its voice from.
        (await session.Page.Locator("html").GetAttributeAsync("lang")).ShouldBe("tr");
    }

    [Fact]
    public async Task Language_stays_English_in_English_browser()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })
            .WaitForAsync(new() { Timeout = 15_000 });

        (await session.Page.Locator("html").GetAttributeAsync("lang")).ShouldBe("en");
    }

    [Fact]
    public async Task Language_is_changed_and_preference_persists_across_reload()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.GetByTestId("language-toggle").ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Gösterge Paneli" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // Language is not a secret (does not conflict with K-047): it is kept
        // in localStorage and persists across reload — and in a new tab.
        await session.Page.ReloadAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Gösterge Paneli" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // The stored preference overrides the browser language.
        (await session.Page.Locator("html").GetAttributeAsync("lang")).ShouldBe("tr");

        // The selector on the Settings screen also shows the same preference.
        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        (await session.Page.GetByTestId("language-select").InputValueAsync()).ShouldBe("tr");
    }

    [Fact]
    public async Task Server_error_in_Turkish_UI_is_shown_without_translation()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "tr-TR");

        // A nonexistent agent produces a 404; the UI shows the server's own
        // text as-is — the API contract is single-language.
        await session.Page.GotoAsync($"{host.UiAddress}/agents/nonexistent-agent");

        await session.Page.GetByRole(AriaRole.Alert).First.WaitForAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Command_palette_navigates_to_screen()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.Keyboard.PressAsync("Control+k");
        await session.Page.GetByTestId("command-palette").WaitForAsync();

        await session.Page.Keyboard.TypeAsync("sessions");
        await session.Page.Keyboard.PressAsync("Enter");

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Sessions" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // The palette closes after navigating.
        (await session.Page.GetByTestId("command-palette").CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Command_palette_closes_with_Esc()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.Keyboard.PressAsync("Control+k");
        await session.Page.GetByTestId("command-palette").WaitForAsync();

        await session.Page.Keyboard.PressAsync("Escape");

        await session.Page.GetByTestId("command-palette").WaitForAsync(
            new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [Fact]
    public async Task Command_palette_returns_focus_to_opening_button_after_Esc()
    {
        // BUG-S4-004: Esc closed the palette but never returned focus
        // anywhere (document.activeElement fell back to <body>).
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.GetByTestId("palette-open").ClickAsync();
        await session.Page.GetByTestId("command-palette").WaitForAsync();

        await session.Page.Keyboard.PressAsync("Escape");

        await session.Page.GetByTestId("command-palette").WaitForAsync(
            new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        var focusIsOnTrigger = await session.Page.EvaluateAsync<bool>(
            "() => document.activeElement === document.querySelector('[data-testid=\"palette-open\"]')");

        focusIsOnTrigger.ShouldBeTrue("Focus did not return to the button that opened the palette after Esc.");
    }

    [Fact]
    public async Task Command_palette_is_filtered_by_role()
    {
        // When the Admin policy fails, the "New agent" command must also
        // disappear from the palette: offering an action the server will
        // reject is worse than not offering it. Hiding it is still a
        // courtesy; the server is what enforces it (Phase 9).
        await using var host = await UiHost.StartAsync(
            configureServices: services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, policy => policy.RequireAssertion(_ => false)));

        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.Keyboard.PressAsync("Control+k");
        await session.Page.GetByTestId("command-palette").WaitForAsync();

        await session.Page.Keyboard.TypeAsync("new agent");

        await session.Page.GetByText("Nothing matches that.").WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task G_A_shortcut_navigates_to_agents_screen()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.Keyboard.PressAsync("g");
        await session.Page.Keyboard.PressAsync("a");

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Shortcut_does_not_trigger_inside_text_field()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync($"{host.UiAddress}/playground");
        await session.Page.GetByTestId("playground-input").WaitForAsync();

        // 🚨 A key typed into a text field belongs to that text field. "ga"
        // here is two letters, not a navigation shortcut.
        await session.Page.GetByTestId("playground-input").ClickAsync();
        await session.Page.Keyboard.TypeAsync("gargle");

        (await session.Page.GetByTestId("playground-input").InputValueAsync()).ShouldBe("gargle");

        // The screen must not have changed.
        session.Page.Url.ShouldContain("/playground");
    }

    [Fact]
    public async Task Shortcut_help_opens_with_question_mark()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, locale: "en-US");

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" }).WaitForAsync();

        await session.Page.Keyboard.PressAsync("?");

        await session.Page.GetByTestId("shortcut-help").WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("Keyboard shortcuts").First.WaitForAsync();
    }

    // --- Phase 55: asynchronous approvals inbox ---

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
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Approvals", Exact = true }).WaitForAsync();

        // The worker leases the job from the queue and waits until it reaches
        // the tool call that requires approval; the screen auto-refreshes every 5 seconds.
        await session.Page.GetByText("cancel_order").WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();

        // The row disappears from the list: the request is no longer Pending.
        await session.Page.GetByText("cancel_order").WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        await session.Page.GetByText("Nothing is waiting").WaitForAsync();

        // The decision queues a NEW run, which also completes. The Runs list
        // must show both the old row (which STAYS in AwaitingApproval, K-014)
        // and the new (Completed) row.
        await session.Page.GotoAsync($"{host.UiAddress}/runs");
        await session.Page.GetByText("awaiting approval", new() { Exact = true }).WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("completed", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });
    }

    // --- Phase 61: client-side tools and embeddable chat ---

    [Fact]
    public async Task Tools_screen_shows_a_client_side_badge_for_a_client_tool()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/tools");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Tools" }).WaitForAsync();

        var card = session.Page.GetByText("read_page_title").First;
        await card.WaitForAsync();

        // The badge appears ONLY on the client-side tool's own card — a
        // server-side tool (e.g. get_order_status) must not carry it.
        await session.Page.GetByText("client-side").WaitForAsync();
    }

    [Fact]
    public async Task Embed_widget_runs_a_client_side_tool_and_completes_the_turn_in_a_real_browser()
    {
        // Relative script src only: the test page is served from the SAME
        // origin as the AgentPrism host (proves the widget's session/tool-call
        // mechanics, not CORS — CorsOptionsTests already covers that at the
        // HTTP level), so the widget's own `new URL(script.src).origin`
        // fallback resolves 'data-server' without the page needing to know
        // the host's dynamically assigned port. The API key is read from a
        // query parameter and interpolated at REQUEST time (not baked into a
        // constant): K-359 validates an `Authorization` header even when no
        // `AuthToken` is configured on the host, so the widget's fetch call
        // needs a REAL RunsWrite-scoped key, not a placeholder string.
        await using var host = await UiHost.StartAsync(
            configureApp: app => app.MapGet("/embed-test", (HttpContext context) => Results.Content(
                $"""
                 <!doctype html>
                 <html><body>
                 <script src="/agentprism/embed/embed.js"
                   data-agent="client-tool-agent" data-api-key="{context.Request.Query["key"]}"></script>
                 <script>
                   window.AgentPrismEmbed.registerTool('read_page_title', () => 'Shopping cart');
                 </script>
                 </body></html>
                 """,
                "text/html")));

        using var api = new HttpClient { BaseAddress = new Uri(host.BaseAddress + "/") };
        using var keyResponse = await api.PostAsJsonAsync(
            "agentprism/api/api-keys",
            new { name = "embed-widget-test", scopes = new[] { "RunsWrite" } });

        keyResponse.EnsureSuccessStatusCode();
        var created = await keyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var apiKey = created.GetProperty("plaintextKey").GetString();

        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.BaseAddress}/embed-test?key={Uri.EscapeDataString(apiKey!)}");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Open chat" }).ClickAsync();
        await session.Page.GetByPlaceholder("Message…").FillAsync("what is the page title?");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();

        // The registered handler runs and the tool result reaches the model —
        // proves the session the widget reserves up front is actually usable
        // (regression coverage for the bug the Phase 61 audit found: the
        // widget used to never set a sessionId, so a pending tool call could
        // never be answered and the turn silently stalled).
        await session.Page.GetByText("Title: Shopping cart").WaitForAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Embed_widget_script_is_served_with_the_real_built_bundle()
    {
        await using var host = await UiHost.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };
        using var response = await http.GetAsync("embed/embed.js");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/javascript");

        var body = await response.Content.ReadAsStringAsync();
        body.Length.ShouldBeGreaterThan(0);

        // The global the embedding page calls after the script tag loads.
        body.ShouldContain("AgentPrismEmbed");
    }

    /// <summary>
    /// Creates a database agent from the UI, then edits its instructions to
    /// produce a second version. Shared precondition for the diff and
    /// experiment tests.
    /// </summary>
    private static async Task CreateAgentWithTwoVersionsAsync(
        UiHost host,
        Session session,
        string name,
        string firstInstructions,
        string secondInstructions)
    {
        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");
        await session.Page.GetByTestId("agent-name").FillAsync(name);
        await session.Page.GetByTestId("agent-instructions").FillAsync(firstInstructions);
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModels.Default);
        await session.Page.GetByTestId("agent-save").ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = name }).WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await session.Page.GetByTestId("agent-instructions").FillAsync(secondInstructions);
        await session.Page.GetByTestId("agent-save").ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = name }).WaitForAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Verifies that the page itself does not shift horizontally at 375px width.
    /// BUG-S4-008: because <c>Panel</c>'s title+action row and <c>Row</c>'s
    /// value column (long URL values) did not wrap, the page as a whole
    /// widened on a narrow screen — not the tables/cards inside it.
    /// </summary>
    private static async Task AssertNoHorizontalOverflowAsync(IPage page, string screen)
    {
        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.ShouldBeLessThanOrEqualTo(0, $"{screen} overflows horizontally at 375px width ({overflow}px).");
    }
}
