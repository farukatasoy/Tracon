namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Providers:OpenAI</c> reaches <see cref="OpenAIProviderOptions"/>.
/// </summary>
public sealed class OpenAIProviderOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Providers:OpenAI:ApiKey"] = "placeholder-not-a-credential",
            ["Tracon:Providers:OpenAI:DefaultModel"] = "gpt-binding-probe",
            ["Tracon:Providers:OpenAI:Endpoint"] = "https://gateway.example.net/openai/v1/",
            ["Tracon:Providers:OpenAI:Organization"] = "org-binding-probe",
            ["Tracon:Providers:OpenAI:Timeout"] = "00:00:45",
            ["Tracon:Providers:OpenAI:EnableResponsesSurface"] = "true",
            ["Tracon:Providers:OpenAI:Models:0:Name"] = "gpt-binding-probe",
        };

        var options = SectionBinding.Bind<OpenAIProviderOptions>(
            values, static (builder, tracon) => builder.UseOpenAI(tracon.GetSection("Providers:OpenAI")));

        options.ApiKey.ShouldBe("placeholder-not-a-credential");
        options.DefaultModel.ShouldBe("gpt-binding-probe");
        options.Endpoint.ShouldBe(new Uri("https://gateway.example.net/openai/v1/"));
        options.Organization.ShouldBe("org-binding-probe");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.EnableResponsesSurface.ShouldBeTrue();
        options.Models.Select(static model => model.Name).ShouldBe(["gpt-binding-probe"]);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Providers:OpenAI", options);
    }
}
