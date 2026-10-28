using System.Net.Http.Json;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The audit screen.
/// </summary>
public sealed class AuditTests(BrowserFixture browsers)
{
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

        await Expect(session.Page.GetByText("agent.create").First).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("agent:audit-e2e").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Verify_chain_shows_the_result_of_the_walk()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        // An agent create writes the first audit entry, so the walk has a chain to check.
        using (var created = await client.PostAsJsonAsync(
            $"{host.Prefix}/api/agents",
            new
            {
                name = "audit-verify-e2e",
                instructions = "test",
                model = new { provider = ScriptedModels.ProviderName, model = ScriptedModels.Default },
                toolNames = Array.Empty<string>(),
            }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/audit");

        // Not walked on open: the panel waits for the operator.
        await Expect(session.Page.GetByTestId("audit-verify-result")).ToHaveCountAsync(0);

        await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Verify chain" }).ClickAsync();

        var result = session.Page.GetByTestId("audit-verify-result");

        await Expect(result).ToBeVisibleAsync();
        await Expect(result).ToContainTextAsync("Valid");
        await Expect(result).ToContainTextAsync("Entries checked");
        await Expect(result).ToContainTextAsync("Unsigned entries");

        // This host has no chain key, so every entry is checked with a plain hash and the screen says so.
        await Expect(session.Page.GetByTestId("audit-verify-unkeyed-warning")).ToBeVisibleAsync();
    }
}
