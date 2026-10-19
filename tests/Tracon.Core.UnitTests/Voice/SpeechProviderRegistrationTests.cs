using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// The rules of <c>AddSpeechSynthesizer</c> and <c>AddSpeechTranscriber</c>: what a
/// name may be, and that a name selects exactly one provider.
/// </summary>
/// <remarks>
/// A keyed registration under a repeated key does not fail: the container resolves
/// the LAST one, so the order of two setup calls would decide silently which engine a
/// tenant's text reaches (the K-663 class). The repeated name is refused at setup.
/// </remarks>
public sealed class SpeechProviderRegistrationTests
{
    [Fact]
    public void A_repeated_name_is_refused_at_setup_instead_of_the_last_one_winning()
    {
        var builder = new ServiceCollection().AddTracon();

        builder.AddSpeechSynthesizer("studio", static _ => new NamedSynthesizer("first"));

        var exception = Should.Throw<InvalidOperationException>(
            () => builder.AddSpeechSynthesizer("studio", static _ => new NamedSynthesizer("second")));

        exception.Message.ShouldContain("'studio'");
        exception.Message.ShouldContain("already registered");
    }

    [Fact]
    public void A_name_that_differs_only_in_letter_case_is_the_same_name()
    {
        var builder = new ServiceCollection().AddTracon();

        builder.AddSpeechSynthesizer("Studio", static _ => new NamedSynthesizer("first"));

        Should.Throw<InvalidOperationException>(
            () => builder.AddSpeechSynthesizer("STUDIO", static _ => new NamedSynthesizer("second")));
    }

    [Fact]
    public async Task Synthesizer_and_transcriber_names_are_separate()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddSpeechSynthesizer("studio", static _ => new NamedSynthesizer("synth"))
            .AddSpeechTranscriber("studio", static _ => new NamedTranscriber("stt"));

        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();

        (await resolver.GetSynthesizerAsync("studio", TestContext.Current.CancellationToken)).ProviderName.ShouldBe("synth");
        (await resolver.GetTranscriberAsync("studio", TestContext.Current.CancellationToken)).ProviderName.ShouldBe("stt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/name")]
    [InlineData("café")]
    public void A_name_outside_the_allowed_characters_is_refused(string name)
    {
        var builder = new ServiceCollection().AddTracon();

        var exception = Should.Throw<ArgumentException>(
            () => builder.AddSpeechSynthesizer(name, static _ => new NamedSynthesizer("x")));

        exception.ParamName.ShouldBe("name");
    }

    [Fact]
    public void A_name_longer_than_64_characters_is_refused_and_64_is_accepted()
    {
        var builder = new ServiceCollection().AddTracon();

        builder.AddSpeechSynthesizer(new string('a', 64), static _ => new NamedSynthesizer("x"));

        Should.Throw<ArgumentException>(
            () => builder.AddSpeechSynthesizer(new string('b', 65), static _ => new NamedSynthesizer("x")));
    }

    [Fact]
    public void A_null_name_or_factory_is_refused()
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<ArgumentException>(() => builder.AddSpeechSynthesizer(null!, static _ => new NamedSynthesizer("x")));
        Should.Throw<ArgumentNullException>(
                () => builder.AddSpeechSynthesizer("studio", (Func<IServiceProvider, ISpeechSynthesizer>)null!))
            .ParamName.ShouldBe("factory");
    }

    [Theory]
    [InlineData("Studio.V2_beta-1", "studio.v2_beta-1")]
    [InlineData("gpu", "gpu")]
    public void A_valid_name_is_folded_to_lower_case(string written, string expected)
    {
        SpeechProviderNames.TryNormalize(written, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    public void An_invalid_name_does_not_normalize(string? written)
        => SpeechProviderNames.TryNormalize(written, out _).ShouldBeFalse();

    [Fact]
    public async Task An_unknown_name_names_the_registered_ones_and_a_missing_default_says_so()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddSpeechSynthesizer("studio", static _ => new NamedSynthesizer("studio"))
            .AddSpeechSynthesizer("gpu", static _ => new NamedSynthesizer("gpu"));

        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();

        var unknown = await Should.ThrowAsync<TraconException>(
            async () => await resolver.GetSynthesizerAsync("cloud", TestContext.Current.CancellationToken));

        unknown.Message.ShouldContain("'cloud'");
        unknown.Message.ShouldContain("gpu, studio");

        var missingDefault = await Should.ThrowAsync<TraconException>(
            async () => await resolver.GetSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken));

        missingDefault.Message.ShouldContain("No default speech synthesizer");

        // A name with characters outside the rule is reported without repeating it.
        var invalid = await Should.ThrowAsync<TraconException>(
            async () => await resolver.GetSynthesizerAsync(new string('x', 65), TestContext.Current.CancellationToken));

        invalid.Message.ShouldNotContain(new string('x', 65));
    }

    [Fact]
    public void A_speech_contract_written_before_tenant_credentials_does_not_support_them()
    {
        ISpeechSynthesizer synthesizer = new NamedSynthesizer("legacy");
        ISpeechTranscriber transcriber = new NamedTranscriber("legacy");

        synthesizer.SupportsTenantCredentials.ShouldBeFalse();
        transcriber.SupportsTenantCredentials.ShouldBeFalse();
    }

    [Fact]
    public async Task The_default_voice_list_refuses_a_credential_instead_of_using_its_own_key()
    {
        var legacy = new NamedSynthesizer("legacy");
        ISpeechSynthesizer synthesizer = legacy;

        (await synthesizer.ListVoicesWithCredentialAsync(null, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        legacy.VoiceLists.ShouldBe(1);

        await Should.ThrowAsync<NotSupportedException>(async () => await synthesizer.ListVoicesWithCredentialAsync(
            new ModelProviderCredential { ApiKey = "tenant-voice-value" },
            TestContext.Current.CancellationToken));

        legacy.VoiceLists.ShouldBe(1);
    }

    [Fact]
    public void The_pricing_reader_written_before_provider_prices_answers_null()
    {
        IVoicePricingReader reader = new LegacyPricing();

        reader.ForSynthesis("studio", "studio-v1", 1000m).ShouldBeNull();
        reader.ForTranscription("studio", "studio-v1", TimeSpan.FromMinutes(1)).ShouldBeNull();
    }

    private sealed class LegacyPricing : IVoicePricingReader
    {
        public string? Currency => "USD";

        public decimal? ForCharacters(decimal characters) => 1m;
    }

    private sealed class NamedSynthesizer(string providerName) : ISpeechSynthesizer
    {
        public int VoiceLists { get; private set; }

        public string ProviderName => providerName;

        public int MaxCharactersPerRequest => 100;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        {
            VoiceLists++;

            return ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
        }
    }

    private sealed class NamedTranscriber(string providerName) : ISpeechTranscriber
    {
        public string ProviderName => providerName;

        public ValueTask<SpeechTranscript> TranscribeAsync(
            Stream audio,
            string mediaType,
            SpeechTranscriptionOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
