using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A second ElevenLabs synthesizer has a concurrency limit of its own, so a batch that voices a podcast does not take the slots a
/// live conversation's turn waits for.
/// </summary>
/// <remarks>
/// The built-in client holds one limit for synthesis, dialogue and transcription. The cases hold that the named client is a client
/// of its own (its own pool), that it is the same provider to everything that decides by provider, and that it changes nothing of
/// the default.
/// </remarks>
public sealed class ElevenLabsNamedSynthesizerTests
{
    [Fact]
    public async Task The_named_synthesizer_is_a_client_of_its_own_and_the_default_is_untouched()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch", static options => options.MaxConcurrentRequests = 4));

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();
        var batch = await resolver.GetSynthesizerAsync("batch", TestContext.Current.CancellationToken);
        var defaultOne = await resolver.GetSynthesizerAsync(name: null, TestContext.Current.CancellationToken);

        batch.ShouldNotBeSameAs(defaultOne, "one client would hold one pool, which is what the second one is for");
        batch.ProviderName.ShouldBe("elevenlabs", "the tenant's policy and key, and the price, are the provider's, not the name's");
        defaultOne.ShouldBeSameAs(provider.GetRequiredService<ISpeechSynthesizer>());

        provider.GetRequiredService<IOptions<VoiceOptions>>().Value.MaxConcurrentRequests
            .ShouldBe(2, "the change is the named client's and never the options of UseVoice");

        (await resolver.GetSynthesizerAsync("batch", TestContext.Current.CancellationToken)).ShouldBeSameAs(batch);
    }

    [Fact]
    public async Task The_named_synthesizer_starts_from_the_options_of_UseVoice()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch"));

        var batch = await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken);

        // The limit of a request is part of what the copy carries.
        batch.MaxCharactersPerRequest.ShouldBe(5000);
    }

    [Fact]
    public async Task Without_UseVoice_the_first_call_for_the_name_says_what_is_missing()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddElevenLabsSynthesizer("batch");

        using var provider = services.BuildServiceProvider();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("UseVoice", Case.Sensitive);
    }

    [Fact]
    public async Task Options_the_named_client_may_not_have_fail_at_the_first_call_and_name_the_option()
    {
        using var provider = Build(static voice => voice.AddElevenLabsSynthesizer("batch", static options => options.MaxConcurrentRequests = 0));

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<ISpeechProviderResolver>().GetSynthesizerAsync("batch", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("concurrent request limit", Case.Sensitive);
        failure.Message.ShouldContain("batch", Case.Sensitive);
    }

    [Fact]
    public void A_second_registration_of_the_same_name_is_refused()
    {
        var services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(
            () => services.AddTracon()
                .UseVoice(static options => options.ApiKey = "k")
                .AddElevenLabsSynthesizer("batch")
                .AddElevenLabsSynthesizer("batch"));
    }

    private static ServiceProvider Build(Action<ITraconBuilder> configure)
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon().UseVoice(static options => options.ApiKey = "k");

        configure(builder);

        return services.BuildServiceProvider();
    }
}
