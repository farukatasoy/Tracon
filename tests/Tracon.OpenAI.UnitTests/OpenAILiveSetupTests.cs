using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// An OpenAI provider whose options name the configuration keys of the key and the address: start-up checks, binding, and
/// what the image and speech clients send after a rotation.
/// </summary>
/// <remarks>
/// The chat path against a compiled agent, the judge, the health check and the failure text are asserted through the real
/// host in <c>LiveSetupKeyRotationTests</c> (functional). The image and speech clients are asserted here against two
/// real loopback servers, because a server answers one request and a second server proves the address moved.
/// </remarks>
#pragma warning disable MEAI001
public sealed class OpenAILiveSetupTests
{
    private const string KeyName = "Platform:OpenAI:Key";
    private const string EndpointName = "Platform:OpenAI:Endpoint";

    private const string ImageAnswer =
        """{"created":1,"data":[{"b64_json":"aGk="}],"output_format":"png"}""";

    [Fact]
    public async Task The_image_generator_sends_the_key_and_the_address_that_are_current_when_it_generates()
    {
        await using var first = StubHttpServer.Respond(200, "OK", ImageAnswer);
        await using var second = StubHttpServer.Respond(200, "OK", ImageAnswer);
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, V1(first)));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        var generator = provider.GetRequiredKeyedService<IImageGenerator>(OpenAIProviderNames.ChatCompletions);

        await generator.GenerateAsync(new ImageGenerationRequest("a red kite"), cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = V1(second);

        // The generator is the one registered at start: the same object, and it still reads the pair when it generates.
        provider.GetRequiredKeyedService<IImageGenerator>(OpenAIProviderNames.ChatCompletions).ShouldBeSameAs(generator);

        await generator.GenerateAsync(new ImageGenerationRequest("a red kite"), cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one-0001");
        (await second.RequestHead).ShouldStartWith("POST /v1/images/generations HTTP/1.1");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two-0002");
    }

    [Fact]
    public async Task The_text_to_speech_client_sends_the_key_and_the_address_that_are_current_when_it_speaks()
    {
        await using var first = StubHttpServer.Respond(200, "OK", "first");
        await using var second = StubHttpServer.Respond(200, "OK", "second");
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, V1(first)));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        using var client = provider.GetRequiredService<OpenAINamedChatClientFactoryCache>()
            .Setup(Options.DefaultName)
            .CreateTextToSpeechClient("tts-1");

        await client.GetAudioAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = V1(second);

        await client.GetAudioAsync("hello again", cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one-0001");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two-0002");
    }

    private const string CompletionBody =
        """{"id":"c1","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"pong"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";

    [Fact]
    public async Task A_compatible_server_that_asks_for_no_key_follows_a_moved_address()
    {
        await using var first = StubHttpServer.Respond(200, "OK", CompletionBody);
        await using var second = StubHttpServer.Respond(200, "OK", CompletionBody);
        var configuration = Configuration((EndpointName, V1(first)));

        await using var provider = BuildCompatible(configuration, options => options.EndpointConfigurationName = EndpointName);

        using var client = provider.GetRequiredService<IModelProviderRegistry>().CreateChatClient(new ModelBinding { Provider = "local", Model = "m" });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")], cancellationToken: TestContext.Current.CancellationToken);

        configuration[EndpointName] = V1(second);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")], cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldStartWith("POST /v1/chat/completions HTTP/1.1");
        (await second.RequestHead).ShouldStartWith("POST /v1/chat/completions HTTP/1.1");
    }

    [Fact]
    public async Task A_compatible_server_that_gets_a_key_by_name_sends_the_key_that_is_current_when_it_is_called()
    {
        await using var first = StubHttpServer.Respond(200, "OK", CompletionBody);
        await using var second = StubHttpServer.Respond(200, "OK", CompletionBody);
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, V1(first)));

        await using var provider = BuildCompatible(
            configuration,
            options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        using var client = provider.GetRequiredService<IModelProviderRegistry>().CreateChatClient(new ModelBinding { Provider = "local", Model = "m" });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")], cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = V1(second);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")], cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one-0001");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two-0002");
    }

    [Fact]
    public async Task A_provider_that_names_no_configuration_key_keeps_one_factory_for_the_life_of_the_host()
    {
        await using var provider = Build(Configuration(), options => options.ApiKey = TestData.ApiKey);

        var setup = provider.GetRequiredService<OpenAINamedChatClientFactoryCache>().Setup(Options.DefaultName);

        setup.Live.ShouldBeNull();
        setup.Current().ShouldBeSameAs(setup.Initial);
        provider.GetRequiredService<OpenAIChatClientFactory>().ShouldBeSameAs(setup.Initial);
    }

    [Fact]
    public async Task The_same_snapshot_gives_both_surfaces_one_factory_and_a_rotation_gives_both_the_new_one()
    {
        var configuration = Configuration((KeyName, "key-one-0001"));

        await using var provider = Build(configuration, options => options.ApiKeyConfigurationName = KeyName);

        var live = provider.GetRequiredService<OpenAINamedChatClientFactoryCache>().Setup(Options.DefaultName).Live.ShouldNotBeNull();
        var first = live.Current();

        live.Current().ShouldBeSameAs(first);

        configuration[KeyName] = "key-two-0002";

        var second = live.Current();

        second.ShouldNotBeSameAs(first);
        live.Current().ShouldBeSameAs(second);
    }

    [Fact]
    public async Task The_provider_lists_the_key_in_use_and_the_one_it_replaced_and_names_the_configuration_key_in_its_diagnostic()
    {
        var configuration = Configuration((KeyName, "key-one-0001"));

        await using var provider = Build(configuration, options => options.ApiKeyConfigurationName = KeyName);

        var model = provider.GetServices<IModelProvider>().Single(static p => string.Equals(p.Name, OpenAIProviderNames.ChatCompletions, StringComparison.Ordinal));

        configuration[KeyName] = "key-two-0002";

        // The rotation is seen when a call is made: the call reads the pair, and the provider reports what it has sent.
        using (var client = model.CreateChatClient(TestData.Binding()))
        {
            _ = client.GetService(typeof(ChatClientMetadata));
        }

        model.SetupApiKeys.ShouldBe(["key-two-0002", "key-one-0001"]);

        var diagnostic = model.ShouldBeAssignableTo<IModelProviderConfigurationDiagnostics>().GetConfigurationDiagnostic().ShouldNotBeNull();

        diagnostic.Key.ShouldBe(KeyName);
        diagnostic.Resolved.ShouldBeTrue();
    }

    [Fact]
    public void A_host_may_start_with_a_name_that_holds_the_key_and_not_without_one()
    {
        var withValue = new OpenAIProviderOptionsValidator(Configuration((KeyName, "key-one-0001")));
        var withoutValue = new OpenAIProviderOptionsValidator(Configuration());

        withValue.Validate(name: null, new OpenAIProviderOptions { ApiKeyConfigurationName = KeyName }).Succeeded.ShouldBeTrue();

        var refused = withoutValue.Validate(name: null, new OpenAIProviderOptions { ApiKeyConfigurationName = KeyName });

        refused.Failed.ShouldBeTrue();
        refused.FailureMessage.ShouldContain(KeyName);
    }

    [Fact]
    public void A_compatible_provider_may_start_with_a_name_that_holds_the_address()
    {
        var validator = new OpenAIProviderOptionsValidator(Configuration((EndpointName, "http://local.example/v1/")));

        validator.Validate("local", new OpenAIProviderOptions { EndpointConfigurationName = EndpointName }).Succeeded.ShouldBeTrue();
        new OpenAIProviderOptionsValidator(Configuration()).Validate("local", new OpenAIProviderOptions { EndpointConfigurationName = EndpointName }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void A_name_of_blanks_is_refused()
    {
        const string blanks = "   ";

        var validator = new OpenAIProviderOptionsValidator(Configuration());

        validator.Validate(name: null, new OpenAIProviderOptions { ApiKey = TestData.ApiKey, ApiKeyConfigurationName = blanks }).Failed.ShouldBeTrue();
        validator.Validate(name: null, new OpenAIProviderOptions { ApiKey = TestData.ApiKey, EndpointConfigurationName = blanks }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void The_two_names_are_read_from_the_configuration_section()
    {
        var section = Configuration(("ApiKeyConfigurationName", KeyName), ("EndpointConfigurationName", EndpointName));
        var options = new OpenAIProviderOptions();

        OpenAIProviderExtensions.Bind(section, options);

        options.ApiKeyConfigurationName.ShouldBe(KeyName);
        options.EndpointConfigurationName.ShouldBe(EndpointName);
    }

    private static string V1(StubHttpServer server) => new Uri(server.BaseAddress, "v1/").ToString();

    private static IConfiguration Configuration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static ServiceProvider BuildCompatible(IConfiguration configuration, Action<OpenAIProviderOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddTracon().UseOpenAICompatible("local", configure);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider Build(IConfiguration configuration, Action<OpenAIProviderOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddTracon()
            .UseOpenAI(configure)
            .UseOpenAIImages(static options => options.Model = "gpt-image-1");

        return services.BuildServiceProvider();
    }
}
