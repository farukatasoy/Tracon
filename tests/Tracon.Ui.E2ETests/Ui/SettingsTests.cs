using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The settings screen: theme, quotas and webhooks.
/// </summary>
public sealed class SettingsTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Dark_theme_toggle_works_and_persists()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        var before = await session.Page.GetAttributeAsync("html", "data-theme");

        await session.Page.GetByTestId("theme-toggle").ClickAsync();

        // Phase 184: read once the theme has moved, not the instant the click
        // returns - the repaint is the page's own work.
        await Expect(session.Page.Locator("html"), "The theme did not change.")
            .Not.ToHaveAttributeAsync("data-theme", before ?? string.Empty);

        var after = await session.Page.GetAttributeAsync("html", "data-theme");

        (after is "light" or "dark").ShouldBeTrue($"Unexpected theme: {after}");

        // The preference lives in localStorage; a reload must preserve it.
        await session.Page.ReloadAsync();
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await Expect(session.Page.Locator("html"), "The theme preference was not preserved across reload.")
            .ToHaveAttributeAsync("data-theme", after!);
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
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        // First switch to "dark" via the top bar toggle: the "light" selection
        // below is then a real change, independent of this machine's system
        // color preference.
        if (!string.Equals(await session.Page.GetAttributeAsync("html", "data-theme"), "dark", StringComparison.Ordinal))
        {
            await session.Page.GetByTestId("theme-toggle").ClickAsync();
        }

        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        // The wrapping <label>'s accessible name concatenates the <select>'s
        // own rendered option text ("ThemeFollow systemLightDark"), so
        // GetByLabel("Theme") never matches exactly; scope by the wrapper
        // instead.
        await session.Page.Locator("label", new() { HasText = "Theme" }).Locator("select")
            .SelectOptionAsync("light");

        await Expect(session.Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");

        // 🚨 Read through the BINDING, not off a `title` attribute. Phase 165
        // moved this description into `Tooltip`, because `title` never showed
        // the current preference to a touch or keyboard user at all. The
        // assertion got stronger in the move: it now proves the text is
        // attached to the control (`aria-describedby`) rather than merely
        // present somewhere on it.
        var description = await session.Page.GetByTestId("theme-toggle").GetAttributeAsync("aria-describedby");

        description.ShouldNotBeNullOrEmpty("The toggle carries no description to read.");

        (await session.Page.Locator($"#{description}").InnerTextAsync())
            .ShouldBe("Theme: light", "The top bar toggle did not learn about the change made in Settings.");
    }

    [Fact]
    public async Task Quota_is_added_and_usage_bar_appears()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add quota" }).ClickAsync();
        await session.Page.GetByTestId("quota-max-runs").FillAsync("100");
        await session.Page.GetByTestId("quota-save").ClickAsync();

        // Once the rule is saved, the row and the usage bar appear.
        await Expect(session.Page.GetByTestId("quota-row").First).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("quota-bar").First).ToBeVisibleAsync();

        await Expect(session.Page.GetByText("0 / 100", new() { Exact = false }).First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Editing a rule's limit keeps the rule disabled, and it cannot move the rule.
    /// </summary>
    /// <remarks>
    /// The form had no switch for <c>enabled</c> and sent a constant <c>true</c>:
    /// raising the limit of a rule disabled through the API turned it back on.
    /// It also let the agent and the period change, and the PUT upserts by that
    /// scope, so an "edit" wrote a second rule. Measured on the sample app on
    /// 2026-10-10.
    /// </remarks>
    [Fact]
    public async Task Editing_a_disabled_quota_keeps_it_disabled_and_in_its_scope()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        using (var created = await api.PutAsJsonAsync(
            "api/quotas",
            new { agentName = "support", period = "Daily", maxRuns = 100, enabled = false }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByTestId("quota-row")
            .GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true })
            .ClickAsync();

        await Expect(session.Page.GetByTestId("quota-agent")).Not.ToBeEditableAsync();
        await Expect(session.Page.GetByTestId("quota-period")).ToBeDisabledAsync();

        await session.Page.GetByTestId("quota-max-runs").FillAsync("150");
        await session.Page.GetByTestId("quota-save").ClickAsync();
        await Expect(session.Page.GetByTestId("quota-max-runs")).ToBeHiddenAsync();

        var quotas = await api.GetFromJsonAsync<JsonElement>("api/quotas");

        quotas.GetArrayLength().ShouldBe(1);
        quotas[0].GetProperty("maxRuns").GetInt64().ShouldBe(150);
        quotas[0].GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// A speech provider is allowed for a restricted tenant from the console, and the
    /// speech call that was refused goes through.
    /// </summary>
    /// <remarks>
    /// The egress choices came from the model catalog only, so a speech vendor could
    /// not be added from the console at all once speech calls obeyed the policy. The
    /// choice is the name the synthesizer REPORTS ('studio-engine'), not the name it
    /// is registered under ('studio').
    /// </remarks>
    [Fact]
    public async Task A_speech_provider_allowed_in_the_console_unblocks_speech_for_the_tenant()
    {
        await using var host = await UiHost.StartAsync(
            configureTracon: static builder => builder.AddSpeechSynthesizer("studio", static _ => new StudioSynthesizer()));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        using (var restricted = await api.PutAsJsonAsync("api/tenants/default/egress", new { allowedProviders = new[] { "scripted" } }))
        {
            restricted.EnsureSuccessStatusCode();
        }

        using (var refused = await api.PostAsJsonAsync("api/voice/speak", new { text = "hello", provider = "studio" }))
        {
            refused.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        }

        await session.Page.GotoAsync($"{host.UiAddress}/settings");

        var option = session.Page.GetByTestId("tenant-egress-option-studio-engine");

        await option.ClickAsync();
        await Expect(option).ToHaveAttributeAsync("aria-pressed", "true");
        await session.Page.GetByTestId("tenant-egress-save").ClickAsync();

        await Expect(session.Page.GetByTestId("tenant-egress-option-studio-engine"))
            .ToHaveAttributeAsync("aria-pressed", "true");

        await WaitUntil.ValueAsync(
            async () => (await api.GetFromJsonAsync<JsonElement>("api/tenants/default/egress"))
                .GetProperty("allowedProviders").EnumerateArray().Select(static name => name.GetString()).ToList(),
            static allowed => allowed.Contains("studio-engine", StringComparer.Ordinal),
            "the console to save the speech provider into the policy");

        using var served = await api.PostAsJsonAsync("api/voice/speak", new { text = "hello", provider = "studio" });

        served.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await served.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A tool policy written in the console is the policy the tenant gets: the tool the administrator
    /// picked is the one the server saved, and a second save with the choice removed saves two empty
    /// lists (a policy that allows nothing), not the absence of a policy.
    /// </summary>
    [Fact]
    public async Task A_tool_policy_saved_in_the_console_is_the_policy_the_server_holds()
    {
        await using var host = await UiHost.StartAsync(
            configureTracon: static builder => builder.AddTool(
                (Func<string>)(static () => "order"),
                name: "get_order",
                description: "Reads an order.",
                configure: static options => options.RequiredPermission = "orders.read"));
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await session.Page.GotoAsync($"{host.UiAddress}/settings");

        await Expect(session.Page.GetByTestId("tenant-tool-policy-state"))
            .ToHaveTextAsync("No policy: every tool is allowed");

        await session.Page.GetByTestId("tenant-tool-restrict").ClickAsync();
        await session.Page.GetByTestId("tenant-tool-option-get_order").ClickAsync();
        await session.Page.GetByTestId("tenant-permission-option-orders.read").ClickAsync();
        await session.Page.GetByTestId("tenant-tool-save").ClickAsync();

        await WaitUntil.ValueAsync(
            async () => (await api.GetFromJsonAsync<JsonElement>("api/tenants/default/tools")),
            static policy => policy.GetProperty("allowedTools").ValueKind == JsonValueKind.Array,
            "the console to save the tool policy");

        var saved = await api.GetFromJsonAsync<JsonElement>("api/tenants/default/tools");

        saved.GetProperty("allowedTools").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["get_order"]);
        saved.GetProperty("grantedPermissions").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["orders.read"]);

        // Remove both choices: two empty lists are a policy that allows nothing, and the panel says so.
        await session.Page.GetByTestId("tenant-tool-option-get_order").ClickAsync();
        await session.Page.GetByTestId("tenant-permission-option-orders.read").ClickAsync();
        await Expect(session.Page.GetByTestId("tenant-tool-policy-empty")).ToBeVisibleAsync();
        await session.Page.GetByTestId("tenant-tool-save").ClickAsync();

        await WaitUntil.ValueAsync(
            async () => (await api.GetFromJsonAsync<JsonElement>("api/tenants/default/tools")),
            static policy => policy.GetProperty("allowedTools").ValueKind == JsonValueKind.Array
                             && policy.GetProperty("allowedTools").GetArrayLength() == 0,
            "the console to save the empty policy");

        // Clearing returns the tenant to no policy.
        await session.Page.GetByTestId("tenant-tool-clear").ClickAsync();
        await Expect(session.Page.GetByTestId("tenant-tool-policy-state"))
            .ToHaveTextAsync("No policy: every tool is allowed");
    }

    /// <summary>A local synthesizer that answers with a fixed MP3 frame.</summary>
    private sealed class StudioSynthesizer : ISpeechSynthesizer
    {
        private static readonly byte[] Mp3 =
            [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB, 0x90, 0x00];

        public string ProviderName => "studio-engine";

        public int MaxCharactersPerRequest => 1000;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SpeechAudio { Data = Mp3, MediaType = "audio/mpeg" });

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }

    [Fact]
    public async Task Webhook_is_added_and_test_send_completes()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" })).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add webhook" }).ClickAsync();
        await session.Page.GetByTestId("webhook-name").FillAsync("order-service");
        await session.Page.GetByTestId("webhook-url").FillAsync("https://example.com/hooks/tracon");

        // 🚨 What goes here is NOT a secret, it is the NAME of the key the
        // secret will be read from.
        await session.Page.GetByTestId("webhook-secret-key")
            .FillAsync("Tracon:WebhookSecrets:order-service");

        await session.Page.GetByTestId("webhook-save").ClickAsync();

        await Expect(session.Page.GetByTestId("webhook-row").First).ToBeVisibleAsync();

        // The key's name appears on screen; its value never does.
        await Expect(session.Page.GetByText("Tracon:WebhookSecrets:order-service", new() { Exact = false })
            .First).ToBeVisibleAsync();

        await session.Page.GetByTestId("webhook-test").First.ClickAsync();
        await Expect(session.Page.GetByTestId("webhook-test-result")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Console_opens_dark_and_still_follows_the_system_when_asked_to()
    {
        // The console is an operations surface, so dark is the product's answer
        // and not the machine's: a console with nothing stored opens dark even
        // on a machine that reports a light system preference. Choosing "follow
        // system" in Settings puts the machine back in charge.
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host, colorScheme: ColorScheme.Light);

        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings", Exact = true })).ToBeVisibleAsync();

        await Expect(session.Page.Locator("html"), "A console with no stored preference did not open dark.").ToHaveAttributeAsync("data-theme", "dark");

        // The wrapping <label>'s accessible name concatenates the <select>'s own
        // rendered option text, so GetByLabel("Theme") never matches exactly;
        // scope by the wrapper instead.
        await session.Page.Locator("label", new() { HasText = "Theme" }).Locator("select")
            .SelectOptionAsync("system");

        await Expect(session.Page.Locator("html"), "Following the system no longer follows the system.").ToHaveAttributeAsync("data-theme", "light");
    }
}
