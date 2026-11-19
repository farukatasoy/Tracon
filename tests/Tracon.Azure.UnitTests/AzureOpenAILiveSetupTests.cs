using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// An Azure OpenAI provider whose options name the configuration keys of the key and the address: start-up checks, binding,
/// the image generator after a rotation, and the rule for a credential factory.
/// </summary>
#pragma warning disable MEAI001
public sealed class AzureOpenAILiveSetupTests
{
    private const string KeyName = "Platform:Azure:Key";
    private const string EndpointName = "Platform:Azure:Endpoint";
    private const string ImageAnswer = """{"created":1,"data":[{"b64_json":"aGk="}]}""";

    [Fact]
    public async Task The_image_generator_sends_the_key_and_the_address_that_are_current_when_it_generates()
    {
        await using var first = StubHttpServer.Respond(200, "OK", ImageAnswer);
        await using var second = StubHttpServer.Respond(200, "OK", ImageAnswer);
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, first.BaseAddress.ToString()));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        var generator = provider.GetRequiredKeyedService<IImageGenerator>(AzureOpenAIProviderNames.AzureOpenAI);

        await generator.GenerateAsync(new ImageGenerationRequest("a red kite"), cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = second.BaseAddress.ToString();

        await generator.GenerateAsync(new ImageGenerationRequest("a red kite"), cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one-0001");
        (await second.RequestHead).ShouldStartWith("POST /openai/v1/images/generations HTTP/1.1");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two-0002");
    }

    private const string CompletionBody =
        """{"id":"c1","object":"chat.completion","created":1,"model":"production-gpt","choices":[{"index":0,"message":{"role":"assistant","content":"pong"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";

    [Fact]
    public async Task A_chat_client_taken_before_the_rotation_sends_the_key_and_the_address_that_are_current_when_it_is_called()
    {
        await using var first = StubHttpServer.Respond(200, "OK", CompletionBody);
        await using var second = StubHttpServer.Respond(200, "OK", CompletionBody);
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, first.BaseAddress.ToString()));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        var model = provider.GetServices<IModelProvider>().Single(static p => string.Equals(p.Name, AzureOpenAIProviderNames.AzureOpenAI, StringComparison.Ordinal));

        // The client an agent keeps from its compile.
        using var client = model.CreateChatClient(TestData.Binding());

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")], cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = second.BaseAddress.ToString();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")], cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one-0001");
        (await second.RequestHead).ShouldStartWith("POST /openai/v1/chat/completions HTTP/1.1");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two-0002");
    }

    [Fact]
    public async Task A_moved_address_builds_the_client_again_and_calls_the_credential_factory_for_it()
    {
        var calls = 0;
        var configuration = Configuration((EndpointName, "https://one.example/"));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.EndpointConfigurationName = EndpointName;
                options.CredentialFactory = () =>
                {
                    Interlocked.Increment(ref calls);

                    return new FakeTokenCredential();
                };
            });

        var live = provider.GetRequiredService<AzureOpenAISetup>().Live.ShouldNotBeNull();
        var started = calls;

        live.Current();

        calls.ShouldBe(started, "the same address is the same factory");

        configuration[EndpointName] = "https://two.example/";

        live.Current();

        calls.ShouldBe(started + 1, "a new address builds a new client, and the credential factory is called for it");
    }

    [Fact]
    public async Task The_health_check_reads_the_key_and_the_address_when_it_runs()
    {
        await using var first = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-a"}]}""");
        await using var second = StubHttpServer.Respond(200, "OK", """{"data":[{"id":"gpt-a"}]}""");
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, first.BaseAddress.ToString()));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        var model = provider.GetServices<IModelProvider>().Single(static p => string.Equals(p.Name, AzureOpenAIProviderNames.AzureOpenAI, StringComparison.Ordinal));
        var health = model.ShouldBeAssignableTo<IModelProviderHealthCheck>();

        (await health.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        configuration[KeyName] = "key-two-0002";
        configuration[EndpointName] = second.BaseAddress.ToString();

        (await health.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one-0001");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two-0002");
    }

    [Fact]
    public async Task The_provider_lists_the_key_in_use_and_the_one_it_replaced()
    {
        var configuration = Configuration((KeyName, "key-one-0001"));

        await using var provider = Build(
            configuration,
            options =>
            {
                options.Endpoint = TestData.Endpoint;
                options.ApiKeyConfigurationName = KeyName;
            });

        var model = provider.GetServices<IModelProvider>().Single(static p => string.Equals(p.Name, AzureOpenAIProviderNames.AzureOpenAI, StringComparison.Ordinal));

        configuration[KeyName] = "key-two-0002";

        using (var client = model.CreateChatClient(TestData.Binding()))
        {
            _ = client.GetService(typeof(ChatClientMetadata));
        }

        model.SetupApiKeys.ShouldBe(["key-two-0002", "key-one-0001"]);
    }

    [Fact]
    public void A_name_for_the_key_cannot_be_given_with_a_credential_factory()
    {
        var validator = new AzureOpenAIProviderOptionsValidator(Configuration((KeyName, "key-one-0001")));

        var refused = validator.Validate(
            name: null,
            new AzureOpenAIProviderOptions
            {
                Endpoint = TestData.Endpoint,
                ApiKeyConfigurationName = KeyName,
                CredentialFactory = static () => throw new InvalidOperationException("not called by a validator"),
            });

        refused.Failed.ShouldBeTrue();
        refused.FailureMessage.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void A_name_for_the_address_works_with_a_credential_factory_and_a_name_for_the_key_may_start_the_host()
    {
        var validator = new AzureOpenAIProviderOptionsValidator(Configuration((KeyName, "key-one-0001"), (EndpointName, TestData.EndpointText)));

        validator.Validate(
            name: null,
            new AzureOpenAIProviderOptions
            {
                EndpointConfigurationName = EndpointName,
                CredentialFactory = static () => throw new InvalidOperationException("not called by a validator"),
            }).Succeeded.ShouldBeTrue();

        validator.Validate(
            name: null,
            new AzureOpenAIProviderOptions { EndpointConfigurationName = EndpointName, ApiKeyConfigurationName = KeyName }).Succeeded.ShouldBeTrue();

        new AzureOpenAIProviderOptionsValidator(Configuration()).Validate(
            name: null,
            new AzureOpenAIProviderOptions { EndpointConfigurationName = EndpointName, ApiKeyConfigurationName = KeyName }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void The_two_names_are_read_from_the_configuration_section()
    {
        var section = Configuration(("ApiKeyConfigurationName", KeyName), ("EndpointConfigurationName", EndpointName));
        var options = new AzureOpenAIProviderOptions();

        AzureOpenAIProviderExtensions.Bind(section, options);

        options.ApiKeyConfigurationName.ShouldBe(KeyName);
        options.EndpointConfigurationName.ShouldBe(EndpointName);
    }

    private static IConfiguration Configuration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration, Action<AzureOpenAIProviderOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddTracon()
            .UseAzureOpenAI(configure)
            .UseAzureOpenAIImages(static options => options.Model = "image-deployment");

        return services.BuildServiceProvider();
    }
}
