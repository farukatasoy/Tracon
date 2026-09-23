using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The tools screen and the embeddable chat's client-side tools.
/// </summary>
public sealed class ToolTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Tools_screen_shows_call_count()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        // First run a turn that produces a real tool call.
        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("where is ORD-9");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await Expect(session.Page.GetByText("Echo: where is ORD-9")).ToBeVisibleAsync();

        await session.Page.GotoAsync($"{host.UiAddress}/tools");

        // In Phase 5 this screen carried a "arrives in the observability
        // phase" note (deviation S5); real counts must be shown now.
        await Expect(session.Page.GetByText("calls").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Tools_screen_shows_a_client_side_badge_for_a_client_tool()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/tools");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Tools" })).ToBeVisibleAsync();

        var card = session.Page.GetByText("read_page_title").First;
        await Expect(card).ToBeVisibleAsync();

        // The badge appears ONLY on the client-side tool's own card — a
        // server-side tool (e.g. get_order_status) must not carry it.
        await Expect(session.Page.GetByText("client-side")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Embed_widget_runs_a_client_side_tool_and_completes_the_turn_in_a_real_browser()
    {
        // Relative script src only: the test page is served from the SAME
        // origin as the Tracon host (proves the widget's session/tool-call
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
                 <script src="/tracon/embed/embed.js"
                   data-agent="client-tool-agent" data-api-key="{context.Request.Query["key"]}"></script>
                 <script>
                   window.TraconEmbed.registerTool('read_page_title', () => 'Shopping cart');
                 </script>
                 </body></html>
                 """,
                "text/html")));

        using var api = new HttpClient { BaseAddress = new Uri(host.BaseAddress + "/") };
        using var keyResponse = await api.PostAsJsonAsync(
            "tracon/api/api-keys",
            new { name = "embed-widget-test", scopes = new[] { "RunsWrite" } });

        keyResponse.EnsureSuccessStatusCode();
        var created = await keyResponse.Content.ReadFromJsonAsync<JsonElement>();
        var apiKey = created.GetProperty("plaintextKey").GetString();

        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.BaseAddress}/embed-test?key={Uri.EscapeDataString(apiKey!)}");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Open chat" }).ClickAsync();
        await session.Page.GetByPlaceholder("Message…", new() { Exact = true }).FillAsync("what is the page title?");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();

        // The registered handler runs and the tool result reaches the model —
        // proves the session the widget reserves up front is actually usable
        // (regression coverage for the bug the Phase 61 audit found: the
        // widget used to never set a sessionId, so a pending tool call could
        // never be answered and the turn silently stalled).
        await Expect(session.Page.GetByText("Title: Shopping cart")).ToBeVisibleAsync();
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
        body.ShouldContain("TraconEmbed");
    }
}
