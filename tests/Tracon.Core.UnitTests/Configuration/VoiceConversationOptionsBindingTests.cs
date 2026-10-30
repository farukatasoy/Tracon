using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Voice:Conversation</c> really reaches <see cref="VoiceConversationOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The section is bound by hand (AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound.
/// </para>
/// <para>
/// 🚨 This section is not bound by <c>AddTracon</c> but by
/// <c>UseVoiceConversation(section)</c>; a test that only called <c>AddTracon</c>
/// would read the defaults and pass for the wrong reason. The absent-section test
/// uses the same composition.
/// </para>
/// </remarks>
public sealed class VoiceConversationOptionsBindingTests
{
    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeVoiceConversation =
        static (builder, section) => builder.UseVoiceConversation(section.GetSection("Voice:Conversation"));

    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Voice:Conversation:MaxConcurrentConnectionsPerTenant"] = "3",
            ["Tracon:Voice:Conversation:MaxConnectionDuration"] = "00:20:00",
            ["Tracon:Voice:Conversation:IdleTimeout"] = "00:01:00",
            ["Tracon:Voice:Conversation:MaxUtteranceDuration"] = "00:00:30",
            ["Tracon:Voice:Conversation:MaxUtteranceBytes"] = "1048576",
            ["Tracon:Voice:Conversation:PersistAudio"] = "true",
            ["Tracon:Voice:Conversation:VoiceId"] = "alloy",
            ["Tracon:Voice:Conversation:OutputMediaType"] = "audio/wav",
            ["Tracon:Voice:Conversation:InputSampleRate"] = "24000",
            ["Tracon:Voice:Conversation:MaxSpokenCharactersPerTurn"] = "2000",
        };

        var options = SectionBinding.Bind<VoiceConversationOptions>(values, ComposeVoiceConversation);

        options.MaxConcurrentConnectionsPerTenant.ShouldBe(3);
        options.MaxConnectionDuration.ShouldBe(TimeSpan.FromMinutes(20));
        options.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxUtteranceDuration.ShouldBe(TimeSpan.FromSeconds(30));
        options.MaxUtteranceBytes.ShouldBe(1024 * 1024);
        options.PersistAudio.ShouldBeTrue();
        string.Equals(options.VoiceId, "alloy", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(options.OutputMediaType, "audio/wav", StringComparison.Ordinal).ShouldBeTrue();
        options.InputSampleRate.ShouldBe(24_000);
        options.MaxSpokenCharactersPerTurn.ShouldBe(2_000);
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Voice:Conversation", options);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<VoiceConversationOptions>(SectionBinding.NoKeys, ComposeVoiceConversation);

        options.MaxConcurrentConnectionsPerTenant.ShouldBe(5);
        options.MaxConnectionDuration.ShouldBe(TimeSpan.FromMinutes(30));
        options.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        options.MaxUtteranceDuration.ShouldBe(TimeSpan.FromSeconds(60));
        options.MaxUtteranceBytes.ShouldBe(8 * 1024 * 1024);
        options.PersistAudio.ShouldBeFalse();
        options.VoiceId.ShouldBeNull();
        string.Equals(options.OutputMediaType, "audio/mpeg", StringComparison.Ordinal).ShouldBeTrue();
        options.InputSampleRate.ShouldBe(16_000);
        options.MaxSpokenCharactersPerTurn.ShouldBe(5_000);
    }
}
