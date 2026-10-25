using Microsoft.Extensions.DependencyInjection;
using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>
/// The shape of the <c>UseGoogleSpeech()</c> registration: the named synthesizer, the provider list it appears in,
/// the settings it refuses and the client it shares with the chat provider.
/// </summary>
public sealed class GoogleSpeechBuilderExtensionsTests
{
    [Fact]
    public async Task The_synthesizer_is_registered_under_its_name_and_listed_with_its_dialogue_limits()
    {
        using var provider = Build(options =>
        {
            options.Model = GoogleSpeechTestData.Model;
            options.MaxDialogueCharactersPerRequest = 777;
        });

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();
        var listed = resolver.Providers.ShouldHaveSingleItem();

        listed.Name.ShouldBe("gemini");
        listed.Kind.ShouldBe(SpeechProviderKind.Synthesizer);
        listed.ProviderName.ShouldBe(GoogleProviderNames.Google);
        listed.SupportsTenantCredentials.ShouldBeTrue();
        listed.Dialogue.ShouldBe(new SpeechDialogueCapabilities { MaxVoices = 2, MaxCharactersPerRequest = 777, SupportsSegmentTimestamps = false });

        var synthesizer = await resolver.GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);
        var dialogue = await resolver.GetDialogueSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        synthesizer.ShouldBeOfType<GoogleSpeechSynthesizer>().ShouldBeSameAs(dialogue);
    }

    [Fact]
    public void A_name_the_caller_chose_is_the_registration_name_and_the_provider_name_stays_google()
    {
        using var provider = Build(options =>
        {
            options.Model = GoogleSpeechTestData.Model;
            options.Name = "Narrator";
        });

        var listed = provider.GetRequiredService<ISpeechProviderResolver>().Providers.ShouldHaveSingleItem();

        listed.Name.ShouldBe("narrator");
        listed.ProviderName.ShouldBe(GoogleProviderNames.Google);
    }

    [Fact]
    public void The_synthesizer_is_not_the_default_unless_asked()
    {
        using var provider = Build(options => options.Model = GoogleSpeechTestData.Model);

        provider.GetService<ISpeechSynthesizer>().ShouldBeNull();
    }

    [Fact]
    public void The_synthesizer_is_the_default_when_asked()
    {
        using var provider = Build(options =>
        {
            options.Model = GoogleSpeechTestData.Model;
            options.UseAsDefault = true;
        });

        provider.GetRequiredService<ISpeechSynthesizer>()
            .ShouldBeSameAs(provider.GetRequiredKeyedService<ISpeechSynthesizer>(
                new SpeechProviderRegistration(SpeechProviderKind.Synthesizer, "gemini")));
    }

    [Fact]
    public void A_default_that_is_already_registered_stays()
    {
        var own = new OtherSynthesizer();
        var services = new ServiceCollection();

        services.AddSingleton<ISpeechSynthesizer>(own);
        services.AddTracon()
            .UseGoogle(GoogleSpeechTestData.SetupKey)
            .UseGoogleSpeech(options =>
            {
                options.Model = GoogleSpeechTestData.Model;
                options.UseAsDefault = true;
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISpeechSynthesizer>().ShouldBeSameAs(own);
    }

    [Fact]
    public async Task The_voices_and_limits_the_options_declare_are_the_synthesizers()
    {
        using var provider = Build(options =>
        {
            options.Model = GoogleSpeechTestData.Model;
            options.MaxCharactersPerRequest = 321;
            options.Voices.Add(new VoiceDescriptor { VoiceId = "Kore", Name = "Kore" });
        });

        var synthesizer = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        synthesizer.MaxCharactersPerRequest.ShouldBe(321);
        (await synthesizer.ListVoicesAsync(TestContext.Current.CancellationToken))
            .Select(static voice => voice.VoiceId).ShouldBe(["Kore"]);
    }

    [Fact]
    public async Task A_change_to_the_options_after_the_call_does_not_change_the_synthesizer()
    {
        GoogleSpeechOptions? captured = null;

        using var provider = Build(options =>
        {
            options.Model = GoogleSpeechTestData.Model;
            captured = options;
        });

        captured!.MaxCharactersPerRequest = 1;
        captured.Voices.Add(new VoiceDescriptor { VoiceId = "Late", Name = "Late" });

        var synthesizer = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        synthesizer.MaxCharactersPerRequest.ShouldBe(5000);
        (await synthesizer.ListVoicesAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_synthesizer_calls_the_client_UseGoogle_built()
    {
        using var provider = Build(options => options.Model = GoogleSpeechTestData.Model);

        var factory = provider.GetRequiredService<GoogleChatClientFactory>();
        var synthesizer = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        // The provider is a service of its own and the registry still lists the one instance.
        provider.GetServices<IModelProvider>().ShouldHaveSingleItem()
            .ShouldBeSameAs(provider.GetRequiredService<GoogleModelProvider>());
        factory.Client.ShouldNotBeNull();
        synthesizer.ShouldBeOfType<GoogleSpeechSynthesizer>();
    }

    [Fact]
    public void A_tenant_credential_gets_one_factory_and_two_credentials_get_two()
    {
        using var provider = Build(options => options.Model = GoogleSpeechTestData.Model);

        var google = provider.GetRequiredService<GoogleModelProvider>();
        var first = new ModelProviderCredential { ApiKey = "tenant-a-key-0000000000" };
        var second = new ModelProviderCredential { ApiKey = "tenant-b-key-0000000000" };

        var a = google.GetTenantFactory(first);

        google.GetTenantFactory(new ModelProviderCredential { ApiKey = first.ApiKey }).ShouldBeSameAs(a);
        google.GetTenantFactory(second).ShouldNotBeSameAs(a);
        a.ShouldNotBeSameAs(provider.GetRequiredService<GoogleChatClientFactory>());
        a.Client.ShouldNotBeSameAs(provider.GetRequiredService<GoogleChatClientFactory>().Client);
    }

    [Fact]
    public void Calling_UseGoogle_twice_still_registers_one_provider_next_to_the_speech()
    {
        var services = new ServiceCollection();

        services.AddTracon()
            .UseGoogle(GoogleSpeechTestData.SetupKey)
            .UseGoogleSpeech(options => options.Model = GoogleSpeechTestData.Model)
            .UseGoogle(options => options.DefaultModel = TestData.Model);

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);
    }

    [Fact]
    public void Using_speech_without_the_provider_fails_when_the_synthesizer_is_resolved_not_when_it_is_registered()
    {
        var services = new ServiceCollection();

        services.AddTracon().UseGoogleSpeech(options => options.Model = GoogleSpeechTestData.Model);

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<ISpeechSynthesizer>(new SpeechProviderRegistration(SpeechProviderKind.Synthesizer, "gemini")));
    }

    [Theory]
    [InlineData("no model", "Model")]
    [InlineData("blank model", "Model")]
    [InlineData("zero characters", "MaxCharactersPerRequest")]
    [InlineData("negative dialogue characters", "MaxDialogueCharactersPerRequest")]
    [InlineData("undefined mode", "DialogueSpeakerMode")]
    [InlineData("bad name", "name")]
    public void A_setting_that_cannot_work_is_refused_when_the_call_is_made(string because, string expected)
    {
        void Configure(GoogleSpeechOptions options)
        {
            options.Model = GoogleSpeechTestData.Model;

            switch (because)
            {
                case "no model":
                    options.Model = null;
                    break;
                case "blank model":
                    options.Model = "  ";
                    break;
                case "zero characters":
                    options.MaxCharactersPerRequest = 0;
                    break;
                case "negative dialogue characters":
                    options.MaxDialogueCharactersPerRequest = -1;
                    break;
                case "undefined mode":
                    options.DialogueSpeakerMode = (GoogleDialogueSpeakerMode)99;
                    break;
                default:
                    options.Name = "not a valid name!";
                    break;
            }
        }

        var builder = new ServiceCollection().AddTracon().UseGoogle(GoogleSpeechTestData.SetupKey);

        Should.Throw<ArgumentException>(() => builder.UseGoogleSpeech(Configure), because)
            .Message.ShouldContain(expected, Case.Insensitive);
    }

    [Fact]
    public void Null_arguments_are_argument_errors()
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<ArgumentNullException>(() => builder.UseGoogleSpeech(null!));
        Should.Throw<ArgumentNullException>(() => GoogleSpeechBuilderExtensions.UseGoogleSpeech(null!, _ => { }));
    }

    private static ServiceProvider Build(Action<GoogleSpeechOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddTracon()
            .UseGoogle(GoogleSpeechTestData.SetupKey)
            .UseGoogleSpeech(configure);

        return services.BuildServiceProvider();
    }

    private sealed class OtherSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "other";

        public int MaxCharactersPerRequest => 10;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
