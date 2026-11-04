using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The size limits of the MCP client that have no meaning at zero are refused at startup, and the two that do keep it.
/// </summary>
/// <remarks>
/// A tool limit of zero kept no tool (the first tool of a server tripped it, with a warning that quoted the limit) and a
/// byte limit of zero trimmed every resource to nothing. Nothing said so at start. The limits that count resources and
/// prompts document that a value below one keeps none, so a host can say "no resources" with them; those two are not
/// ranged.
/// </remarks>
public sealed class McpLimitRangeValidationTests
{
    [Theory]
    [InlineData("MaxToolsPerServer", "0")]
    [InlineData("MaxToolsPerServer", "-1")]
    [InlineData("MaxResourceBytesPerResource", "0")]
    [InlineData("MaxResourceBytesPerResource", "-1")]
    [InlineData("MaxResourceBytesTotal", "0")]
    [InlineData("MaxResourceBytesTotal", "-1")]
    public void A_limit_that_keeps_nothing_and_says_nothing_is_refused_at_startup(string member, string value)
    {
        using var provider = Build(($"Tracon:Mcp:{member}", value));

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain($"{nameof(TraconMcpOptions)}.{member}", Case.Sensitive);
        failure.Message.ShouldContain("at least 1", Case.Sensitive);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void A_sliding_lifetime_of_zero_or_less_is_refused_at_startup_as_its_documentation_says(string value)
    {
        using var provider = Build(("Tracon:Mcp:OAuthTokenSlidingLifetime", value));

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain(nameof(TraconMcpOptions.OAuthTokenSlidingLifetime), Case.Sensitive);
        failure.Message.ShouldContain("greater than zero", Case.Sensitive);
    }

    [Theory]
    [InlineData("MaxResourcesPerServer", "0")]
    [InlineData("MaxPromptsPerServer", "0")]
    [InlineData("MaxToolsPerServer", "1")]
    [InlineData("MaxResourceBytesPerResource", "1")]
    [InlineData("MaxResourceBytesTotal", "1")]
    public void A_limit_with_a_documented_zero_and_the_smallest_valid_limit_start(string member, string value)
    {
        using var provider = Build(($"Tracon:Mcp:{member}", value));

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
