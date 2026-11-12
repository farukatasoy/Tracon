namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Providers:AzureOpenAI</c> reaches <see cref="AzureOpenAIProviderOptions"/>.
/// </summary>
/// <remarks>
/// <see cref="AzureOpenAIProviderOptions.CredentialFactory"/> is a delegate; only code can set it, so no key names it and it
/// is exempt, with the assertion that a configuration leaves it unset.
/// </remarks>
public sealed class AzureOpenAIProviderOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Providers:AzureOpenAI:Endpoint"] = "https://binding-probe.openai.azure.com/",
            ["Tracon:Providers:AzureOpenAI:ApiKey"] = "placeholder-not-a-credential",
            ["Tracon:Providers:AzureOpenAI:DefaultDeployment"] = "binding-probe-deployment",
            ["Tracon:Providers:AzureOpenAI:Audience"] = "https://cognitiveservices.azure.us/.default",
            ["Tracon:Providers:AzureOpenAI:Timeout"] = "00:00:45",
            ["Tracon:Providers:AzureOpenAI:Models:0:Name"] = "binding-probe-deployment",
        };

        var options = SectionBinding.Bind<AzureOpenAIProviderOptions>(
            values, static (builder, tracon) => builder.UseAzureOpenAI(tracon.GetSection("Providers:AzureOpenAI")));

        options.Endpoint.ShouldBe(new Uri("https://binding-probe.openai.azure.com/"));
        options.ApiKey.ShouldBe("placeholder-not-a-credential");
        options.DefaultDeployment.ShouldBe("binding-probe-deployment");
        options.Audience.ShouldBe("https://cognitiveservices.azure.us/.default");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.Models.Select(static model => model.Name).ShouldBe(["binding-probe-deployment"]);
        options.CredentialFactory.ShouldBeNull();
        OptionsKeyCoverage.ShouldCoverEveryMember(
            values, "Tracon:Providers:AzureOpenAI", options, nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }
}
