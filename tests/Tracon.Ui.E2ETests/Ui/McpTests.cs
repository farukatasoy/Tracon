using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// The MCP screen.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class McpTests(BrowserFixture browsers)
{
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

        // The placeholder "github" also matches "Tracon:McpSecrets:GithubToken";
        // an exact match must be requested.
        await session.Page.GetByPlaceholder("github", new() { Exact = true }).FillAsync("sample");
        await session.Page.GetByPlaceholder("https://mcp.example.com/mcp")
            .FillAsync("https://mcp.sample.test/mcp");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByText("sample").First.WaitForAsync(new() { Timeout = 10_000 });

        // The approval badge must appear: MCP tools require approval by default.
        await session.Page.GetByText("approval", new() { Exact = true }).First.WaitForAsync();
    }
}
