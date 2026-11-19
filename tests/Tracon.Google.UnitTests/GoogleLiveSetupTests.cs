using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Google.UnitTests.Infrastructure;

#pragma warning disable MEAI001
namespace Tracon.Google.UnitTests;

/// <summary>
/// A Google provider set up with the configuration keys of its key and its address sends the value that is current when a
/// call starts: chat, the health check, the image generator and the speech synthesizer.
/// </summary>
/// <remarks>
/// <para>
/// The provider builds its own Google client, so these tests read the wire: a local server records the address a request
/// came to and the <c>x-goog-api-key</c> header it carried. The configuration is changed in memory and the next call
/// reads it; no test waits for a file watcher.
/// </para>
/// <para>
/// A client, an image generator or a synthesizer is built once, before the rotation, and called again after it. That is
/// the case that matters: what a rotation must reach is the object the host already holds.
/// </para>
/// </remarks>
public sealed class GoogleLiveSetupTests
{
    private const string KeyName = "Platform:Google:Key";
    private const string EndpointName = "Platform:Google:Endpoint";
    private const string StartupKey = "startup-key-0000000000";
    private const string KeyOne = "key-one-0000000000000";
    private const string KeyTwo = "key-two-0000000000000";
    private const string ImageModel = "gemini-test-image";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public async Task A_chat_client_built_before_a_rotation_sends_the_new_key_to_the_new_address_on_its_next_call()
    {
        await using var first = new GoogleLoopbackServer();
        await using var second = new GoogleLoopbackServer();
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, first.BaseAddress.ToString()));

        using var setup = new Harness(configuration, first.BaseAddress, NamesBoth);
        using var client = setup.Provider.CreateChatClient(TestData.Binding());

        await Ask(client);

        configuration[KeyName] = KeyTwo;
        configuration[EndpointName] = second.BaseAddress.ToString();

        await Ask(client);

        // One client, two calls: the key and the address of each came from the configuration at the start of that call.
        first.Keys.ShouldBe([KeyOne]);
        second.Keys.ShouldBe([KeyTwo]);
        second.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldEndWith($"/models/{TestData.Model}:generateContent");
    }

    [Fact]
    public async Task A_value_that_is_blank_or_not_an_address_keeps_the_last_valid_pair()
    {
        await using var first = new GoogleLoopbackServer();
        await using var second = new GoogleLoopbackServer();
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, first.BaseAddress.ToString()));

        using var setup = new Harness(configuration, first.BaseAddress, NamesBoth);
        using var client = setup.Provider.CreateChatClient(TestData.Binding());

        await Ask(client);

        configuration[KeyName] = "  ";
        configuration[EndpointName] = "not an address";

        await Ask(client);

        configuration[KeyName] = KeyTwo;
        configuration[EndpointName] = second.BaseAddress.ToString();

        await Ask(client);

        first.Keys.ShouldBe([KeyOne, KeyOne]);
        second.Keys.ShouldBe([KeyTwo]);
    }

    [Fact]
    public async Task A_host_that_names_only_the_address_keeps_its_key_and_follows_the_address()
    {
        await using var first = new GoogleLoopbackServer();
        await using var second = new GoogleLoopbackServer();
        var configuration = Configuration((EndpointName, first.BaseAddress.ToString()));

        using var setup = new Harness(configuration, first.BaseAddress, static options => options.EndpointConfigurationName = EndpointName);
        using var client = setup.Provider.CreateChatClient(TestData.Binding());

        await Ask(client);

        configuration[EndpointName] = second.BaseAddress.ToString();

        await Ask(client);

        first.Keys.ShouldBe([StartupKey]);
        second.Keys.ShouldBe([StartupKey]);
    }

    [Fact]
    public async Task A_provider_that_names_no_configuration_key_keeps_the_key_and_the_address_it_started_with()
    {
        await using var server = new GoogleLoopbackServer();
        await using var other = new GoogleLoopbackServer();
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, other.BaseAddress.ToString()));

        using var setup = new Harness(configuration, server.BaseAddress, static _ => { });

        setup.Setup.Live.ShouldBeNull();

        using var client = setup.Provider.CreateChatClient(TestData.Binding());

        await Ask(client);

        configuration[KeyName] = KeyTwo;

        await Ask(client);

        server.Keys.ShouldBe([StartupKey, StartupKey]);
        other.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tenant_key_without_an_address_of_its_own_goes_to_the_setup_address_that_is_current()
    {
        await using var first = new GoogleLoopbackServer();
        await using var second = new GoogleLoopbackServer();
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, first.BaseAddress.ToString()));

        using var setup = new Harness(configuration, first.BaseAddress, NamesBoth);

        configuration[EndpointName] = second.BaseAddress.ToString();

        using var client = setup.Provider.CreateChatClient(
            TestData.Binding(),
            new ModelProviderCredential { ApiKey = "tenant-key-0000000000" });

        await Ask(client);

        first.Requests.ShouldBeEmpty();
        second.Keys.ShouldBe(["tenant-key-0000000000"]);
    }

    [Fact]
    public void A_setting_the_provider_refuses_stops_the_build_of_the_client_and_not_its_first_call()
    {
        using var setup = new Harness(Configuration((KeyName, KeyOne)), new Uri("http://127.0.0.1:9/"), NamesBoth);

        var refused = Should.Throw<TraconException>(() => setup.Provider.CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("google.safety.unknown", "BLOCK_NONE")))));

        refused.Message.ShouldContain("google.safety.unknown");
    }

    [Fact]
    public async Task The_health_check_sends_the_key_and_goes_to_the_address_that_are_current()
    {
        await using var first = new GoogleLoopbackServer();
        await using var second = new GoogleLoopbackServer();
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, first.BaseAddress.ToString()));

        using var setup = new Harness(configuration, first.BaseAddress, NamesBoth);

        (await setup.Provider.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        configuration[KeyName] = KeyTwo;
        configuration[EndpointName] = second.BaseAddress.ToString();

        var health = await setup.Provider.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.Status.ShouldBe(ModelProviderHealthStatus.Healthy);
        first.Keys.ShouldBe([KeyOne]);
        second.Keys.ShouldBe([KeyTwo]);
        second.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldEndWith("/models");
    }

    [Fact]
    public async Task The_keys_the_provider_reports_are_the_one_in_use_first_and_the_one_it_replaced_next()
    {
        await using var server = new GoogleLoopbackServer();
        var configuration = Configuration((KeyName, KeyOne));

        using var setup = new Harness(configuration, server.BaseAddress, static options => options.ApiKeyConfigurationName = KeyName);
        using var client = setup.Provider.CreateChatClient(TestData.Binding());

        await Ask(client);

        setup.Provider.SetupApiKeys[0].ShouldBe(KeyOne);

        configuration[KeyName] = KeyTwo;

        await Ask(client);

        setup.Provider.SetupApiKeys.Take(2).ShouldBe([KeyTwo, KeyOne]);
        ((ILiveSetupModelProvider)setup.Provider).SetupKeysChange.ShouldBeTrue();
    }

    [Fact]
    public void A_provider_without_a_live_setup_reports_its_key_and_says_it_does_not_change()
    {
        using var setup = new Harness(Configuration(), new Uri("http://127.0.0.1:9/"), static _ => { });

        setup.Provider.SetupApiKeys.ShouldBe([StartupKey]);
        ((ILiveSetupModelProvider)setup.Provider).SetupKeysChange.ShouldBeFalse();
    }

    [Fact]
    public void The_configuration_diagnostic_names_the_configuration_key_the_host_gave()
    {
        using var setup = new Harness(Configuration((KeyName, KeyOne)), new Uri("http://127.0.0.1:9/"), NamesBoth);

        var diagnostic = setup.Provider.GetConfigurationDiagnostic().ShouldNotBeNull();

        diagnostic.Key.ShouldBe(KeyName);
        diagnostic.Resolved.ShouldBeTrue();
    }

    [Fact]
    public async Task An_image_generator_registered_before_a_rotation_sends_the_new_key_and_stays_the_same_instance()
    {
        await using var first = new GoogleLoopbackServer(static _ => GoogleSpeechTestData.AnswerJson([("image/png", Png)], ImageModel));
        await using var second = new GoogleLoopbackServer(static _ => GoogleSpeechTestData.AnswerJson([("image/png", Png)], ImageModel));
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, first.BaseAddress.ToString()));

        using var provider = Build(configuration, first.BaseAddress, static builder => builder.UseGoogleImages(static options => options.Model = ImageModel));

        var generator = provider.GetRequiredKeyedService<IImageGenerator>(GoogleProviderNames.Google);

        _ = await generator.GenerateAsync(new ImageGenerationRequest("a lighthouse"), new ImageGenerationOptions { ModelId = ImageModel }, TestContext.Current.CancellationToken);

        configuration[KeyName] = KeyTwo;
        configuration[EndpointName] = second.BaseAddress.ToString();

        var response = await provider.GetRequiredKeyedService<IImageGenerator>(GoogleProviderNames.Google)
            .GenerateAsync(new ImageGenerationRequest("a lighthouse"), new ImageGenerationOptions { ModelId = ImageModel }, TestContext.Current.CancellationToken);

        // The image resolver keeps one telemetry ring for each registered instance, so a rotation must not replace it.
        provider.GetRequiredKeyedService<IImageGenerator>(GoogleProviderNames.Google).ShouldBeSameAs(generator);
        response.Contents.OfType<DataContent>().ShouldHaveSingleItem();
        first.Keys.ShouldBe([KeyOne]);
        second.Keys.ShouldBe([KeyTwo]);
    }

    [Fact]
    public async Task A_speech_synthesizer_resolved_before_a_rotation_sends_the_new_key_on_its_next_call()
    {
        var answer = (GoogleRecordedRequest _) => GoogleSpeechTestData.AnswerJson([("audio/L16;codec=pcm;rate=24000", GoogleSpeechTestData.Pcm())]);

        await using var first = new GoogleLoopbackServer(answer);
        await using var second = new GoogleLoopbackServer(answer);
        var configuration = Configuration((KeyName, KeyOne), (EndpointName, first.BaseAddress.ToString()));

        using var provider = Build(
            configuration,
            first.BaseAddress,
            static builder => builder.UseGoogleSpeech(static options => options.Model = GoogleSpeechTestData.Model));

        var synthesizer = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        _ = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);

        configuration[KeyName] = KeyTwo;
        configuration[EndpointName] = second.BaseAddress.ToString();

        _ = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);

        first.Keys.ShouldBe([KeyOne]);
        second.Keys.ShouldBe([KeyTwo]);
    }

    [Fact]
    public async Task A_tenant_call_to_the_speech_synthesizer_keeps_the_key_of_its_own_credential()
    {
        var answer = (GoogleRecordedRequest _) => GoogleSpeechTestData.AnswerJson([("audio/L16;codec=pcm;rate=24000", GoogleSpeechTestData.Pcm())]);

        await using var server = new GoogleLoopbackServer(answer);
        var configuration = Configuration((KeyName, KeyOne));

        using var provider = Build(
            configuration,
            server.BaseAddress,
            static builder => builder.UseGoogleSpeech(static options => options.Model = GoogleSpeechTestData.Model));

        var synthesizer = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        configuration[KeyName] = KeyTwo;

        _ = await synthesizer.SynthesizeAsync(
            GoogleSpeechTestData.Speak() with { Credential = new ModelProviderCredential { ApiKey = "tenant-key-0000000000" } },
            TestContext.Current.CancellationToken);

        server.Keys.ShouldBe(["tenant-key-0000000000"]);
    }

    [Fact]
    public async Task An_image_generator_picks_its_client_when_each_call_starts()
    {
        var first = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(Png, "image/png"));
        var second = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(Png, "image/png"));
        var current = GoogleSpeechTestData.CreateClient(first, "image-key-one-000000");
        var generator = new GoogleImageGenerator(() => current);

        _ = await generator.GenerateAsync(new ImageGenerationRequest("a lighthouse"), new ImageGenerationOptions { ModelId = ImageModel }, TestContext.Current.CancellationToken);

        current = GoogleSpeechTestData.CreateClient(second, "image-key-two-000000");

        _ = await generator.GenerateAsync(new ImageGenerationRequest("a lighthouse"), new ImageGenerationOptions { ModelId = ImageModel }, TestContext.Current.CancellationToken);

        first.Requests.ShouldHaveSingleItem().ApiKey.ShouldBe("image-key-one-000000");
        second.Requests.ShouldHaveSingleItem().ApiKey.ShouldBe("image-key-two-000000");
    }

    [Fact]
    public async Task A_speech_synthesizer_picks_the_setup_client_when_each_call_starts_and_a_tenant_never_reads_it()
    {
        var setupHandler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(), "audio/L16;codec=pcm;rate=24000"));
        var tenantHandler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(), "audio/L16;codec=pcm;rate=24000"));
        var reads = 0;

        var synthesizer = new GoogleSpeechSynthesizer(
            new GoogleSpeechOptions { Model = GoogleSpeechTestData.Model },
            () =>
            {
                reads++;

                return GoogleSpeechTestData.CreateClient(setupHandler, "setup-key-read-" + reads);
            },
            credential => GoogleSpeechTestData.CreateClient(tenantHandler, credential.ApiKey));

        _ = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);
        _ = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);
        _ = await synthesizer.SynthesizeAsync(
            GoogleSpeechTestData.Speak() with { Credential = new ModelProviderCredential { ApiKey = "tenant-key-0000000000" } },
            TestContext.Current.CancellationToken);

        reads.ShouldBe(2);
        setupHandler.Requests.Select(static request => request.ApiKey).ShouldBe(["setup-key-read-1", "setup-key-read-2"]);
        tenantHandler.Requests.ShouldHaveSingleItem().ApiKey.ShouldBe("tenant-key-0000000000");
    }

    [Fact]
    public void The_validator_accepts_a_host_that_starts_with_a_name_that_has_a_value_and_no_literal_key()
    {
        var validator = new GoogleProviderOptionsValidator(Configuration((KeyName, KeyOne)));

        validator.Validate(name: null, new GoogleProviderOptions { ApiKeyConfigurationName = KeyName }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_validator_refuses_a_name_that_has_no_value_when_there_is_no_literal_key_and_names_the_configuration_key()
    {
        var validator = new GoogleProviderOptionsValidator(Configuration());

        var refused = validator.Validate(name: null, new GoogleProviderOptions { ApiKeyConfigurationName = KeyName });

        refused.Failed.ShouldBeTrue();
        string.Join('\n', refused.Failures!).ShouldContain(KeyName);
    }

    [Fact]
    public void The_validator_refuses_a_name_whose_value_is_not_a_key_a_header_can_carry()
    {
        var validator = new GoogleProviderOptionsValidator(Configuration((KeyName, "key\nwith a line break")));

        validator.Validate(name: null, new GoogleProviderOptions { ApiKeyConfigurationName = KeyName }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void The_validator_accepts_a_name_that_has_no_value_yet_when_a_literal_key_is_set()
    {
        var validator = new GoogleProviderOptionsValidator(Configuration());

        validator.Validate(name: null, TestData.Options(options => options.ApiKeyConfigurationName = KeyName)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void The_validator_refuses_a_name_of_blanks_for_the_key_and_for_the_address()
    {
        var validator = new GoogleProviderOptionsValidator(Configuration());

        var key = validator.Validate(name: null, TestData.Options(options => options.ApiKeyConfigurationName = "   "));
        var address = validator.Validate(name: null, TestData.Options(options => options.EndpointConfigurationName = "   "));

        key.Failed.ShouldBeTrue();
        string.Join('\n', key.Failures!).ShouldContain(nameof(GoogleProviderOptions.ApiKeyConfigurationName));
        address.Failed.ShouldBeTrue();
        string.Join('\n', address.Failures!).ShouldContain(nameof(GoogleProviderOptions.EndpointConfigurationName));
    }

    [Fact]
    public void A_validator_built_without_a_configuration_behaves_as_it_did_for_a_literal_key()
    {
        var validator = new GoogleProviderOptionsValidator();

        validator.Validate(name: null, TestData.Options()).Succeeded.ShouldBeTrue();
        validator.Validate(name: null, new GoogleProviderOptions { ApiKeyConfigurationName = KeyName }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Bind_copies_both_configuration_key_names()
    {
        var section = Configuration(
            ("ApiKey", StartupKey),
            ("ApiKeyConfigurationName", KeyName),
            ("EndpointConfigurationName", EndpointName));
        var options = new GoogleProviderOptions();

        GoogleProviderExtensions.Bind(section, options);

        options.ApiKey.ShouldBe(StartupKey);
        options.ApiKeyConfigurationName.ShouldBe(KeyName);
        options.EndpointConfigurationName.ShouldBe(EndpointName);
    }

    [Fact]
    public void A_host_that_binds_the_names_from_its_section_gets_a_live_setup_and_one_shared_factory()
    {
        var configuration = Configuration(
            (KeyName, KeyOne),
            ("Tracon:Providers:Google:ApiKeyConfigurationName", KeyName));
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseGoogle(configuration.GetSection(GoogleProviderOptions.SectionName));

        using var provider = services.BuildServiceProvider();

        var setup = provider.GetRequiredService<GoogleSetup>();

        setup.Live.ShouldNotBeNull();
        provider.GetRequiredService<GoogleChatClientFactory>().ShouldBeSameAs(setup.Initial);
        setup.Initial.SetupApiKey.ShouldBe(KeyOne);
    }

    private static void NamesBoth(GoogleProviderOptions options)
    {
        options.ApiKeyConfigurationName = KeyName;
        options.EndpointConfigurationName = EndpointName;
    }

    private static IConfigurationRoot Configuration(params (string Name, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration, Uri endpoint, Action<ITraconBuilder> configureTracon)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);

        var builder = services.AddTracon().UseGoogle(options =>
        {
            options.ApiKey = StartupKey;
            options.Endpoint = endpoint;
            NamesBoth(options);
        });

        configureTracon(builder);

        return services.BuildServiceProvider();
    }

    private static async Task Ask(IChatClient client)
    {
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe(GoogleLoopbackServer.Sentence);
    }

    /// <summary>A provider on a live setup, built the way <c>UseGoogle</c> builds it.</summary>
    private sealed class Harness : IDisposable
    {
        public Harness(IConfiguration configuration, Uri endpoint, Action<GoogleProviderOptions> configure)
        {
            var options = TestData.Options(o =>
            {
                o.ApiKey = StartupKey;
                o.Endpoint = endpoint;
                configure(o);
            });

            Setup = GoogleSetup.Create(options, loggerFactory: null, configuration);
            Provider = new GoogleModelProvider(
                GoogleProviderNames.Google,
                Setup.Initial,
                [new ModelDescriptor { Name = TestData.Model }],
                healthCheckOptions: options,
                liveSetup: Setup.Live);
        }

        public GoogleSetup Setup { get; }

        public GoogleModelProvider Provider { get; }

        public void Dispose() => Setup.Initial.Dispose();
    }
}
