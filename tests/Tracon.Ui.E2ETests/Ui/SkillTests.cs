using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The skills screen.
/// </summary>
public sealed class SkillTests(BrowserFixture browsers)
{
    private const string PinSkill = "pin-demo";

    /// <summary>The argument schema is part of what a grant pins, so the review shows it.</summary>
    private const string PinSchema = """{"type":"object","properties":{"invoice":{"type":"string"}}}""";

    [Fact]
    public async Task Skill_created_from_UI_is_listed()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/skills/new");

        await session.Page.GetByPlaceholder("invoice-analysis", new() { Exact = true }).FillAsync("invoice-review");
        await session.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description" })
            .FillAsync("Reviews invoices.");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Skills" })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("invoice-review", new() { Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// The form grants what the administrator read: the review shows the script,
    /// the grant carries its hash, and a change behind the grant marks the row
    /// stale instead of leaving it looking active.
    /// </summary>
    [Fact]
    public async Task A_grant_pins_the_reviewed_content_and_turns_stale_when_it_changes()
    {
        await using var host = await StartWithScriptsAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await SaveSkillAsync(api, "echo reviewed-v1", PinSchema);
        var reviewedHash = await ReadScriptHashAsync(api);

        var review = await ReviewAsync(session, host);
        await Expect(review.GetByText("echo reviewed-v1", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(review.GetByText(PinSchema, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(review.GetByText(reviewedHash, new() { Exact = true })).ToBeVisibleAsync();
        await review.GetByRole(AriaRole.Button, new() { Name = "Grant", Exact = true }).ClickAsync();

        var row = session.Page.Locator("tr").Filter(new() { HasText = PinSkill });
        await Expect(row.GetByText("current", new() { Exact = true })).ToBeVisibleAsync();

        var grants = await api.GetFromJsonAsync<JsonElement>("api/skill-script-grants");
        grants.GetArrayLength().ShouldBe(1);
        grants[0].GetProperty("contentHash").GetString().ShouldBe(reviewedHash);

        await SaveSkillAsync(api, "echo changed-v2", PinSchema);
        await session.Page.ReloadAsync();

        await Expect(row.GetByText("stale", new() { Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// The window between reading and granting: the content changes after the
    /// review, and the grant is refused rather than pinned to content nobody
    /// looked at. The review is cleared, so a second attempt starts with a read.
    /// </summary>
    [Fact]
    public async Task A_grant_for_content_that_changed_after_the_review_is_refused()
    {
        await using var host = await StartWithScriptsAsync();
        await using var session = await Session.OpenAsync(browsers, host);
        using var api = new HttpClient { BaseAddress = new Uri(host.UiAddress + "/") };

        await SaveSkillAsync(api, "echo reviewed-v1");

        var review = await ReviewAsync(session, host);
        await Expect(review.GetByText("echo reviewed-v1", new() { Exact = true })).ToBeVisibleAsync();

        await SaveSkillAsync(api, "echo written-after-review");
        await review.GetByRole(AriaRole.Button, new() { Name = "Grant", Exact = true }).ClickAsync();

        await Expect(session.Page.GetByRole(AriaRole.Alert).First).ToContainTextAsync("Content changed");
        await Expect(review).ToBeHiddenAsync();

        var grants = await api.GetFromJsonAsync<JsonElement>("api/skill-script-grants");
        grants.GetArrayLength().ShouldBe(0);
    }

    /// <summary>
    /// A name registered in code opens read only: the form shows the code skill -
    /// the one that runs - and offers no save that would write it over a stored
    /// row. Deleting a stored copy is the one action left.
    /// </summary>
    [Fact]
    public async Task A_skill_defined_in_code_opens_read_only()
    {
        await using var host = await UiHost.StartAsync(configureTracon: static tracon => tracon.AddSkill(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = "code-policy",
            Description = "Defined in code.",
            Instructions = "Follow the code policy.",
        }));
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/skills/code-policy/edit");

        await Expect(session.Page.GetByTestId("skill-code-notice")).ToBeVisibleAsync();
        await Expect(session.Page.GetByPlaceholder("invoice-analysis", new() { Exact = true })).ToBeDisabledAsync();
        await Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToHaveCountAsync(0);
        await Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "Delete stored copy", Exact = true })).ToBeVisibleAsync();
    }

    private static Task<UiHost> StartWithScriptsAsync()
        => UiHost.StartAsync(configureTracon: static tracon => tracon.UseSkillScripts(static options =>
        {
            options.PlatformIsolationAcknowledged = true;
            options.AllowStoredScripts = true;
            options.Interpreters["sh"] = "/bin/sh";
        }));

    /// <summary>Fills the grant form for the demo script and opens its review.</summary>
    private static async Task<ILocator> ReviewAsync(Session session, UiHost host)
    {
        await session.Page.GotoAsync($"{host.UiAddress}/skills");
        await session.Page.GetByPlaceholder("invoice-analysis", new() { Exact = true }).FillAsync(PinSkill);
        await session.Page.GetByPlaceholder("total", new() { Exact = true }).FillAsync("hello");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Review content", Exact = true }).ClickAsync();

        var review = session.Page.GetByRole(AriaRole.Region, new() { Name = "What this grant will run", Exact = true });
        await Expect(review).ToBeVisibleAsync();

        return review;
    }

    private static async Task SaveSkillAsync(HttpClient api, string content, string? schema = null)
    {
        using var response = await api.PutAsJsonAsync($"api/skills/{PinSkill}", new
        {
            name = PinSkill,
            description = "Skill for the grant review tests.",
            instructions = "Run the script when asked.",
            enabled = true,
            resources = Array.Empty<object>(),
            scripts = new[] { new { name = "hello", extension = "sh", content, parametersSchema = schema } },
        });

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadScriptHashAsync(HttpClient api)
    {
        var skill = await api.GetFromJsonAsync<JsonElement>($"api/skills/{PinSkill}");

        return skill.GetProperty("scripts")[0].GetProperty("contentHash").GetString().ShouldNotBeNull();
    }
}
