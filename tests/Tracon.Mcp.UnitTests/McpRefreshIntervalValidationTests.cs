using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// <see cref="TraconMcpOptions.RefreshInterval"/> must fail at startup when
/// the discovery loop cannot wait for it (F-278).
/// </summary>
/// <remarks>
/// The discovery loop waits with <c>Task.Delay</c> inside a hosted service. A
/// negative value threw there and, under .NET's default
/// <c>BackgroundServiceExceptionBehavior</c> (<c>StopHost</c>), stopped the
/// host after startup; zero turned the loop into a busy loop. The test goes
/// through the real registration path to <see cref="IStartupValidator"/>.
/// </remarks>
public sealed class McpRefreshIntervalValidationTests
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("00:00:00.0005")]
    [InlineData("50.00:00:00")]
    public void Refresh_interval_the_loop_cannot_wait_for_is_checked_at_startup(string value)
    {
        using var provider = Build(("Tracon:Mcp:RefreshInterval", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconMcpOptions.RefreshInterval));
    }

    [Fact]
    public void Refresh_interval_is_not_checked_while_discovery_is_off()
    {
        using var provider = Build(("Tracon:Mcp:Enabled", "false"), ("Tracon:Mcp:RefreshInterval", "00:00:00"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Default_refresh_interval_starts()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddTracon().UseMcp(configuration.GetSection(TraconMcpOptions.SectionName), configure: null);

        return services.BuildServiceProvider();
    }
}
