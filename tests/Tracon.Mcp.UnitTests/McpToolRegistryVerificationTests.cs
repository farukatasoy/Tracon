using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// <c>UseMcp()</c>'s <see cref="McpToolRegistry"/> must pass
/// <c>ToolRegistrationValidationService</c>'s check (Phase 102.1 / Manual
/// Case 9) rather than being mistaken for an unrecognized <c>IToolRegistry</c>
/// replacement — the exact failure the check exists to catch.
/// </summary>
/// <remarks>
/// The hosted service is resolved and started through REAL DI, the same way
/// <c>WebApplication.StartAsync</c> would — not constructed by hand — so this
/// proves the actual registered service accepts <c>McpToolRegistry</c>, not a
/// hand-picked stand-in for it.
/// </remarks>
public sealed class McpToolRegistryVerificationTests
{
    [Fact]
    public async Task Real_DI_startup_does_not_reject_the_MCP_registry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTracon().UseMcp();

        await using var provider = services.BuildServiceProvider();

        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(TestContext.Current.CancellationToken);
        }

        provider.GetRequiredService<IToolRegistry>().ShouldBeOfType<McpToolRegistry>();
    }
}
