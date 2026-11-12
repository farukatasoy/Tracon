namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Providers:OpenAI:Live</c> reaches <see cref="OpenAILiveOptions"/>.
/// </summary>
public sealed class OpenAILiveOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Providers:OpenAI:Live:Model"] = "gpt-live-binding-probe",
            ["Tracon:Providers:OpenAI:Live:Voice"] = "binding-probe-voice",
            ["Tracon:Providers:OpenAI:Live:Endpoint"] = "https://gateway.example.net/realtime/",
            ["Tracon:Providers:OpenAI:Live:MaxAppendCharacters"] = "640",
            ["Tracon:Providers:OpenAI:Live:Timeout"] = "00:01:15",
        };

        var options = SectionBinding.Bind<OpenAILiveOptions>(
            values,
            static (builder, tracon) => builder.UseOpenAI("placeholder-not-a-credential")
                .UseOpenAILive(tracon.GetSection("Providers:OpenAI:Live")));

        options.Model.ShouldBe("gpt-live-binding-probe");
        options.Voice.ShouldBe("binding-probe-voice");
        options.Endpoint.ShouldBe(new Uri("https://gateway.example.net/realtime/"));
        options.MaxAppendCharacters.ShouldBe(640);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(75));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Providers:OpenAI:Live", options);
    }
}
