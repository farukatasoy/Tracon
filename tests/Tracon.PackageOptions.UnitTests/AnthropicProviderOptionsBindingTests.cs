namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Providers:Anthropic</c> reaches <see cref="AnthropicProviderOptions"/>.
/// </summary>
public sealed class AnthropicProviderOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Providers:Anthropic:ApiKey"] = "placeholder-not-a-credential",
            ["Tracon:Providers:Anthropic:DefaultModel"] = "claude-binding-probe",
            ["Tracon:Providers:Anthropic:Endpoint"] = "https://gateway.example.net/anthropic/",
            ["Tracon:Providers:Anthropic:DefaultMaxOutputTokens"] = "8192",
            ["Tracon:Providers:Anthropic:Timeout"] = "00:00:45",
            ["Tracon:Providers:Anthropic:MaxRetries"] = "3",
            ["Tracon:Providers:Anthropic:Models:0:Name"] = "claude-binding-probe",
        };

        var options = SectionBinding.Bind<AnthropicProviderOptions>(
            values, static (builder, tracon) => builder.UseAnthropic(tracon.GetSection("Providers:Anthropic")));

        options.ApiKey.ShouldBe("placeholder-not-a-credential");
        options.DefaultModel.ShouldBe("claude-binding-probe");
        options.Endpoint.ShouldBe(new Uri("https://gateway.example.net/anthropic/"));
        options.DefaultMaxOutputTokens.ShouldBe(8192);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.MaxRetries.ShouldBe(3);
        options.Models.Select(static model => model.Name).ShouldBe(["claude-binding-probe"]);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Providers:Anthropic", options);
    }
}
