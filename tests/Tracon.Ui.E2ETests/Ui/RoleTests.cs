using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Tracon.Ui.E2ETests.Infrastructure;

namespace Tracon.Ui.E2ETests.Ui;

/// <summary>
/// What a reader-role user can and cannot see.
/// </summary>
[Collection(ConsoleScreens.Name)]
public sealed class RoleTests(BrowserFixture browsers)
{
    [Fact]
    public async Task Write_buttons_are_hidden_for_reader_role()
    {
        // Phase 9: when the Admin policy fails, the UI must hide write
        // buttons — server-side authorization is still the only real
        // enforcement, this only prevents a bad experience (show it, get a 403).
        await using var host = await UiHost.StartAsync(
            configureServices: services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, policy => policy.RequireAssertion(_ => false)));

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

    [Fact]
    public async Task Reader_role_is_refused_on_admin_screens_rather_than_shown_an_empty_one()
    {
        // 🚨 The misreading this closes: a reader who opened /audit used to see
        // "Nothing is recorded yet" and conclude the trail was empty. The
        // server is still the enforcement; this is the explanation.
        await using var host = await UiHost.StartAsync(
            configureServices: services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, policy => policy.RequireAssertion(_ => false)));

        await using var session = await Session.OpenAsync(browsers, host);

        foreach (var path in new[] { "audit", "diagnostics" })
        {
            await session.Page.GotoAsync($"{host.UiAddress}/{path}");
            await session.Page.GetByTestId("unauthorized").WaitForAsync(new() { Timeout = 30_000 });

            (await session.Page.GetByText("Nothing is recorded yet", new() { Exact = true }).CountAsync()).ShouldBe(
                0,
                $"/{path} showed an empty state to a reader instead of refusing.");
        }
    }
}
