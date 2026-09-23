using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The console shell: it loads, serves its assets and its CSP, and asks for a token when one is required.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class ShellTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Ui_opens_and_home_screen_renders()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        // Dashboard is the landing screen (Phase 20); Agents stays first in the side menu.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        (await session.Page.TitleAsync()).ShouldBe("Tracon");

        // All management screens must be in the navigation bar.
        foreach (var screen in new[] { "Agents", "Dashboard", "Playground", "Sessions", "Workflows", "Jobs", "Evals", "Runs", "Tools", "Skills", "Models", "MCP", "Approvals", "Settings" })
        {
            (await session.Page.GetByRole(AriaRole.Link, new() { Name = screen }).CountAsync())
                .ShouldBeGreaterThan(0, $"Link '{screen}' was not found.");
        }
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
    public async Task Shell_CSP_allows_every_inline_script_it_ships_by_hash()
    {
        // The shell ships one inline script - the early theme paint that runs
        // before the stylesheet arrives - while script-src said 'self' and
        // nothing else, so the browser blocked it on EVERY page load. Nothing
        // broke visibly: theme.ts writes the same attribute later, so the only
        // symptoms were a console error and a theme flash for anyone who had
        // chosen a non-default theme.
        //
        // The hashes are recomputed here from the HTML that was actually
        // served, so this cannot go stale when the script changes - which is
        // exactly how a written-down hash would have failed.
        await using var host = await UiHost.StartAsync();

        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri(host.UiAddress));

        response.Headers.TryGetValues("Content-Security-Policy", out var values).ShouldBeTrue();
        var csp = values!.Single();
        var html = await response.Content.ReadAsStringAsync();

        var inlineScripts = InlineScriptBodies(html);

        inlineScripts.ShouldNotBeEmpty(
            "The shell used to carry an inline theme-paint script. If it was removed on purpose, " +
            "remove this test with it; if it was lost by accident, the early paint is gone.");

        foreach (var body in inlineScripts)
        {
            var hash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));

            csp.ShouldContain(
                $"'sha256-{hash}'",
                customMessage: "An inline script the shell ships is not allowed by its own CSP, " +
                               "so the browser blocks it on every page load.");
        }

        // The hash is what makes the inline script run; the blanket keyword
        // would make every future injected script run too.
        csp.ShouldNotContain("'unsafe-inline'; script-src");
        csp[..csp.IndexOf("style-src", StringComparison.Ordinal)].ShouldNotContain("'unsafe-inline'");
    }

    [Fact]
    public async Task Shell_loads_with_no_console_error()
    {
        // 79 E2E tests passed while every page load logged a CSP violation,
        // because not one of them looked at the console. This one does.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var errors = new List<string>();

        session.Page.Console += (_, message) =>
        {
            if (string.Equals(message.Type, "error", StringComparison.Ordinal))
            {
                lock (errors)
                {
                    errors.Add(message.Text);
                }
            }
        };

        await session.Page.GotoAsync($"{host.UiAddress}/agents");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents", Exact = true })).ToBeVisibleAsync();

        lock (errors)
        {
            errors.ShouldBeEmpty();
        }
    }

    /// <summary>The exact text a browser hashes for each inline script in the document.</summary>
    /// <remarks>
    /// Everything between the opening tag's <c>&gt;</c> and the closing
    /// <c>&lt;/script&gt;</c>, byte for byte. A script with a <c>src</c> is not
    /// inline and never needs a hash.
    /// </remarks>
    private static List<string> InlineScriptBodies(string html)
    {
        var bodies = new List<string>();
        var cursor = 0;

        while (html.IndexOf("<script", cursor, StringComparison.OrdinalIgnoreCase) is var open && open >= 0)
        {
            var openEnd = html.IndexOf('>', open);
            var close = openEnd < 0 ? -1 : html.IndexOf("</script>", openEnd, StringComparison.OrdinalIgnoreCase);

            if (close < 0)
            {
                break;
            }

            var tag = html.AsSpan(open, openEnd - open);
            var body = html[(openEnd + 1)..close];

            if (!tag.Contains(" src=", StringComparison.OrdinalIgnoreCase) && body.Length > 0)
            {
                bodies.Add(body);
            }

            cursor = close + "</script>".Length;
        }

        return bodies;
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
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        failures.ShouldBeEmpty();

        // A deep route must also load the shell; the client performs the redirect.
        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("/panel", new() { Exact = false }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Shell_opens_and_asks_for_token_when_required()
    {
        await using var host = await UiHost.StartAsync(authToken: "secret-token");
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        // The shell is exempt from the bearer token layer; otherwise the user
        // could never see the screen where the token is entered.
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Access token required" })).ToBeVisibleAsync();

        await session.Page.GetByLabel("Token").FillAsync("secret-token");
        var authenticated = session.Page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/agents", StringComparison.Ordinal) &&
            response.Status == StatusCodes.Status200OK);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await authenticated;

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
    }
}
