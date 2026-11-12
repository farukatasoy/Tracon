namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Providers:Google</c> reaches <see cref="GoogleProviderOptions"/>.
/// </summary>
public sealed class GoogleProviderOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Providers:Google:ApiKey"] = "placeholder-not-a-credential",
            ["Tracon:Providers:Google:DefaultModel"] = "gemini-binding-probe",
            ["Tracon:Providers:Google:Endpoint"] = "https://gateway.example.net/google/",
            ["Tracon:Providers:Google:ApiVersion"] = "v1beta2",
            ["Tracon:Providers:Google:Timeout"] = "00:00:45",
            ["Tracon:Providers:Google:Models:0:Name"] = "gemini-binding-probe",
        };

        var options = SectionBinding.Bind<GoogleProviderOptions>(
            values, static (builder, tracon) => builder.UseGoogle(tracon.GetSection("Providers:Google")));

        options.ApiKey.ShouldBe("placeholder-not-a-credential");
        options.DefaultModel.ShouldBe("gemini-binding-probe");
        options.Endpoint.ShouldBe(new Uri("https://gateway.example.net/google/"));
        options.ApiVersion.ShouldBe("v1beta2");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.Models.Select(static model => model.Name).ShouldBe(["gemini-binding-probe"]);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Providers:Google", options);
    }
}
