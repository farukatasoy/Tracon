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
}
