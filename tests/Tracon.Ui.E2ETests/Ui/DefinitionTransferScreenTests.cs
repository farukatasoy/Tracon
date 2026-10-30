using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The Transfer screen: a file leaves one installation, is read in the other's console, and is written only by the
/// role that may write it.
/// </summary>
public sealed class DefinitionTransferScreenTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Admin_checks_a_file_reads_the_change_and_applies_it()
    {
        await using var source = await UiHost.StartAsync();
        await using var target = await UiHost.StartAsync();

        using var sourceClient = new HttpClient { BaseAddress = new Uri(source.UiAddress + "/") };
        using var targetClient = new HttpClient { BaseAddress = new Uri(target.UiAddress + "/") };

        await SaveAgentAsync(sourceClient, "moved-agent", "First wording.");

        var file = Path.Combine(Path.GetTempPath(), $"tracon-definitions-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(file, await sourceClient.GetStringAsync(new Uri("api/definitions/export?agent=moved-agent", UriKind.Relative)));

            await using var session = await Session.OpenAsync(browsers, target);

            await session.Page.GotoAsync($"{target.UiAddress}/transfer");
            await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Transfer", Exact = true })).ToBeVisibleAsync();

            // Nothing can be applied before a file has been checked.
            await Expect(session.Page.GetByTestId("transfer-apply")).ToBeDisabledAsync();

            await session.Page.GetByTestId("transfer-file").SetInputFilesAsync(file);
            await session.Page.GetByTestId("transfer-check").ClickAsync();

            var row = session.Page.GetByTestId("transfer-row-agent-moved-agent");

            await Expect(row).ToBeVisibleAsync();
            await Expect(row.GetByText("Create", new() { Exact = true })).ToBeVisibleAsync();

            // The check wrote nothing.
            using (var before = await targetClient.GetAsync(new Uri("api/agents/moved-agent", UriKind.Relative)))
            {
                before.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
            }

            await session.Page.GetByTestId("transfer-apply").ClickAsync();

            await Expect(session.Page.GetByTestId("transfer-written")).ToContainTextAsync("agent:moved-agent");

            using (var after = await targetClient.GetAsync(new Uri("api/agents/moved-agent", UriKind.Relative)))
            {
                after.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
            }

            // The agent changes in the source; the same screen now reads a new version and shows the two wordings.
            await SaveAgentAsync(sourceClient, "moved-agent", "Second wording.", update: true);

            // The same path again, as a person who edited the file and chose it once more: the screen reads it afresh.
            await File.WriteAllTextAsync(file, await sourceClient.GetStringAsync(new Uri("api/definitions/export?agent=moved-agent", UriKind.Relative)));

            await session.Page.GetByTestId("transfer-file").SetInputFilesAsync(file);
            await session.Page.GetByTestId("transfer-check").ClickAsync();

            await Expect(row.GetByText("New version", new() { Exact = true })).ToBeVisibleAsync();

            await row.GetByRole(AriaRole.Button, new() { Name = "moved-agent" }).ClickAsync();

            await Expect(session.Page.GetByText("\"instructions\": \"First wording.\"")).ToBeVisibleAsync();
            await Expect(session.Page.GetByText("\"instructions\": \"Second wording.\"")).ToBeVisibleAsync();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Operator_can_check_a_file_and_cannot_apply_it()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/transfer");

        await Expect(session.Page.GetByTestId("transfer-check")).ToBeVisibleAsync();
        await Expect(session.Page.GetByTestId("transfer-apply")).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("Applying a file needs the administrator role.")).ToBeVisibleAsync();
    }

    private static async Task SaveAgentAsync(HttpClient client, string name, string instructions, bool update = false)
    {
        var request = new AgentDefinitionRequest
        {
            Name = name,
            Instructions = instructions,
            Model = new ModelBinding { Provider = ScriptedModels.ProviderName, Model = ScriptedModels.Default },
        };

        using var response = update
            ? await client.PutAsJsonAsync(new Uri($"api/agents/{name}", UriKind.Relative), request)
            : await client.PostAsJsonAsync(new Uri("api/agents", UriKind.Relative), request);

        response.EnsureSuccessStatusCode();
    }
}
