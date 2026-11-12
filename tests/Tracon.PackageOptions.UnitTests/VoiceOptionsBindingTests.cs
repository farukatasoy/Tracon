namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Voice</c> reaches <see cref="VoiceOptions"/>.
/// </summary>
public sealed class VoiceOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // The provider name is read without regard to case, so another spelling of it is a value that differs from
            // the shipped one and still passes the validator.
            ["Tracon:Voice:Provider"] = "ElevenLabs",
            ["Tracon:Voice:ApiKey"] = "placeholder-not-a-credential",
            ["Tracon:Voice:Endpoint"] = "https://voice.example.net/",
            ["Tracon:Voice:DefaultVoiceId"] = "binding-probe-voice",
            ["Tracon:Voice:SynthesisModelId"] = "binding-probe-synthesis",
            ["Tracon:Voice:TranscriptionModelId"] = "binding-probe-transcription",
            ["Tracon:Voice:OutputFormat"] = "mp3_22050_32",
            ["Tracon:Voice:MaxCharactersPerRequest"] = "4000",
            ["Tracon:Voice:DialogueModelId"] = "binding-probe-dialogue",
            ["Tracon:Voice:MaxDialogueCharactersPerRequest"] = "1500",
            ["Tracon:Voice:MaxConcurrentRequests"] = "3",
            ["Tracon:Voice:RequireApproval"] = "true",
            ["Tracon:Voice:Timeout"] = "00:01:30",
        };

        var options = SectionBinding.Bind<VoiceOptions>(
            values, static (builder, tracon) => builder.UseVoice(tracon.GetSection("Voice")));

        options.Provider.ShouldBe("ElevenLabs");
        options.ApiKey.ShouldBe("placeholder-not-a-credential");
        options.Endpoint.ShouldBe(new Uri("https://voice.example.net/"));
        options.DefaultVoiceId.ShouldBe("binding-probe-voice");
        options.SynthesisModelId.ShouldBe("binding-probe-synthesis");
        options.TranscriptionModelId.ShouldBe("binding-probe-transcription");
        options.OutputFormat.ShouldBe("mp3_22050_32");
        options.MaxCharactersPerRequest.ShouldBe(4000);
        options.DialogueModelId.ShouldBe("binding-probe-dialogue");
        options.MaxDialogueCharactersPerRequest.ShouldBe(1500);
        options.MaxConcurrentRequests.ShouldBe(3);
        options.RequireApproval.ShouldBeTrue();
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(90));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Voice", options);
    }
}
