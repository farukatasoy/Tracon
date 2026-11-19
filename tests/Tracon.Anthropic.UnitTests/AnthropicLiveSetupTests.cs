using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Anthropic.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// A provider set up with the configuration keys of its key and its address reads both when a call starts: the validator
/// lets the host start on the name alone, the options bind from configuration, and the chat client, the health check, the
/// diagnostic and the list of keys to mask follow a rotation.
/// </summary>
/// <remarks>
/// The wire tests use real sockets, as the other Anthropic wire tests do: the request head the server read is the proof of
/// which key and which address a call used. The configuration is changed in memory; no test waits for a file watcher.
/// </remarks>
public sealed class AnthropicLiveSetupTests
{
    private const string KeyName = "Platform:Anthropic:Key";
    private const string EndpointName = "Platform:Anthropic:Endpoint";
    private const string ModelsBody = """{"data":[{"id":"claude-test"}]}""";

    [Fact]
    public void A_host_can_start_on_the_name_of_the_key_alone_when_the_name_has_a_value()
    {
        var validator = new AnthropicProviderOptionsValidator(Configuration((KeyName, "key-one")));

        validator.Validate(name: null, new AnthropicProviderOptions { ApiKeyConfigurationName = KeyName }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_literal_key_starts_the_host_while_the_name_has_no_value_yet()
    {
        var validator = new AnthropicProviderOptionsValidator(Configuration());

        validator.Validate(
                name: null,
                new AnthropicProviderOptions { ApiKey = TestData.ApiKey, ApiKeyConfigurationName = KeyName })
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_name_with_no_value_and_no_literal_key_stops_the_host_and_names_the_configuration_key(bool configurationIsRegistered)
    {
        var validator = new AnthropicProviderOptionsValidator(configurationIsRegistered ? Configuration(("Other:Key", "key-one")) : null);

        var result = validator.Validate(name: null, new AnthropicProviderOptions { ApiKeyConfigurationName = KeyName });

        result.Failed.ShouldBeTrue();

        var text = string.Join('\n', result.Failures!);

        text.ShouldContain(nameof(AnthropicProviderOptions.ApiKey));
        text.ShouldContain(KeyName);
    }

    [Fact]
    public void A_name_whose_value_cannot_be_sent_does_not_give_the_host_a_key_and_the_failure_does_not_repeat_the_value()
    {
        var validator = new AnthropicProviderOptionsValidator(Configuration((KeyName, "key-\nwith-a-line-break")));

        var result = validator.Validate(name: null, new AnthropicProviderOptions { ApiKeyConfigurationName = KeyName });

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures!).ShouldNotContain("with-a-line-break");
    }

    [Theory]
    [InlineData(nameof(AnthropicProviderOptions.ApiKeyConfigurationName))]
    [InlineData(nameof(AnthropicProviderOptions.EndpointConfigurationName))]
    public void A_name_of_only_white_space_is_refused(string member)
    {
        var options = new AnthropicProviderOptions { ApiKey = TestData.ApiKey };

        if (string.Equals(member, nameof(AnthropicProviderOptions.ApiKeyConfigurationName), StringComparison.Ordinal))
        {
            options.ApiKeyConfigurationName = "   ";
        }
        else
        {
            options.EndpointConfigurationName = "   ";
        }

        var result = new AnthropicProviderOptionsValidator(Configuration()).Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures!).ShouldContain(member);
    }

    [Fact]
    public void Binding_copies_both_configuration_names()
    {
        var section = Configuration(
            (nameof(AnthropicProviderOptions.ApiKeyConfigurationName), KeyName),
            (nameof(AnthropicProviderOptions.EndpointConfigurationName), EndpointName));
        var options = new AnthropicProviderOptions();

        AnthropicProviderExtensions.Bind(section, options);

        options.ApiKeyConfigurationName.ShouldBe(KeyName);
        options.EndpointConfigurationName.ShouldBe(EndpointName);
    }

    [Fact]
    public void Names_written_in_the_provider_section_start_the_host_without_a_literal_key()
    {
        var configuration = Configuration(
            ($"{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKeyConfigurationName)}", KeyName),
            (KeyName, "key-one"));

        using var provider = Build(configuration, builder => builder.UseAnthropic(configuration.GetSection(AnthropicProviderOptions.SectionName)));

        ModelProviderOf(provider).SetupApiKeys.ShouldBe(["key-one"]);
    }

    [Fact]
    public void The_factory_a_live_setup_started_with_is_the_one_the_container_hands_out()
    {
        using var provider = Build(
            Configuration((KeyName, "key-one")),
            builder => builder.UseAnthropic(options => options.ApiKeyConfigurationName = KeyName));

        provider.GetRequiredService<AnthropicChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<AnthropicSetup>().Initial);
    }

    [Fact]
    public async Task A_rotated_key_and_address_are_what_the_next_call_of_the_same_chat_client_sends_and_the_old_key_is_still_masked()
    {
        await using var first = StubHttpServer.Respond(200, "OK", CapturingAnthropicHandler.Response);
        await using var second = StubHttpServer.Respond(200, "OK", CapturingAnthropicHandler.Response);
        var configuration = Configuration((KeyName, "key-one"), (EndpointName, first.BaseAddress.ToString()));

        using var provider = Build(configuration, builder => builder.UseAnthropic(options =>
        {
            options.ApiKeyConfigurationName = KeyName;
            options.EndpointConfigurationName = EndpointName;
            options.MaxRetries = 0;
        }));

        var model = ModelProviderOf(provider);
        using var chatClient = model.CreateChatClient(TestData.Binding());

        model.SetupApiKeys.ShouldBe(["key-one"]);

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two";
        configuration[EndpointName] = second.BaseAddress.ToString();

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one\r\n");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two\r\n");

        // The key now in use first, then the one it replaced: a call that began before the rotation is masked too.
        model.SetupApiKeys.ShouldBe(["key-two", "key-one"]);
    }

    [Fact]
    public async Task A_provider_that_names_nothing_keeps_the_key_it_started_with_whatever_the_configuration_holds()
    {
        await using var server = StubHttpServer.Respond(200, "OK", CapturingAnthropicHandler.Response);
        var configuration = Configuration((KeyName, "ignored-because-no-name-is-set"));

        using var provider = Build(configuration, builder => builder.UseAnthropic(TestData.ApiKey, options =>
        {
            options.Endpoint = server.BaseAddress;
            options.MaxRetries = 0;
        }));

        var model = ModelProviderOf(provider);
        using var chatClient = model.CreateChatClient(TestData.Binding());

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldContain($"Authorization: Bearer {TestData.ApiKey}\r\n");
        model.SetupApiKeys.ShouldBe([TestData.ApiKey]);
        model.GetConfigurationDiagnostic().ShouldNotBeNull().Key.ShouldBe($"{AnthropicProviderOptions.SectionName}:ApiKey");
    }

    [Fact]
    public async Task The_health_check_of_a_live_provider_uses_the_key_and_the_address_that_are_current_when_it_runs()
    {
        await using var first = StubHttpServer.Respond(200, "OK", ModelsBody);
        await using var second = StubHttpServer.Respond(200, "OK", ModelsBody);
        var configuration = Configuration((KeyName, "key-one"), (EndpointName, new Uri(first.BaseAddress, "v1").ToString()));

        using var provider = Build(configuration, builder => builder.UseAnthropic(options =>
        {
            options.ApiKeyConfigurationName = KeyName;
            options.EndpointConfigurationName = EndpointName;
        }));

        var model = ModelProviderOf(provider);

        (await model.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        configuration[KeyName] = "key-two";
        configuration[EndpointName] = new Uri(second.BaseAddress, "v1").ToString();

        (await model.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        var firstHead = await first.RequestHead;
        var secondHead = await second.RequestHead;

        firstHead.ShouldContain("Authorization: Bearer key-one\r\n");
        secondHead.ShouldStartWith("GET /v1/models HTTP/1.1");
        secondHead.ShouldContain("Authorization: Bearer key-two\r\n");
    }

    [Fact]
    public async Task A_health_check_built_on_a_target_reads_the_target_again_every_time_it_runs()
    {
        await using var first = StubHttpServer.Respond(200, "OK", ModelsBody);
        await using var second = StubHttpServer.Respond(200, "OK", ModelsBody);
        var target = new AnthropicProviderHealthCheck.HealthTarget("key-one", new Uri(first.BaseAddress, "v1"), Timeout: null);
        var check = new AnthropicProviderHealthCheck(AnthropicProviderNames.Anthropic, () => target);

        (await check.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        target = new AnthropicProviderHealthCheck.HealthTarget("key-two", new Uri(second.BaseAddress, "v1"), Timeout: null);

        (await check.CheckHealthAsync(TestContext.Current.CancellationToken)).Status.ShouldBe(ModelProviderHealthStatus.Healthy);

        (await first.RequestHead).ShouldContain("Authorization: Bearer key-one\r\n");
        (await second.RequestHead).ShouldContain("Authorization: Bearer key-two\r\n");
    }

    [Fact]
    public void The_diagnostic_of_a_live_provider_names_the_configuration_key_the_host_gave()
    {
        using var provider = Build(
            Configuration((KeyName, "key-one")),
            builder => builder.UseAnthropic(options => options.ApiKeyConfigurationName = KeyName));

        var diagnostic = ModelProviderOf(provider).GetConfigurationDiagnostic().ShouldNotBeNull();

        diagnostic.Key.ShouldBe(KeyName);
        diagnostic.Resolved.ShouldBeTrue();
    }

    [Fact]
    public void A_provider_setting_that_cannot_be_used_still_stops_the_compilation_when_the_setup_is_live()
    {
        using var provider = Build(
            Configuration((KeyName, "key-one")),
            builder => builder.UseAnthropic(options => options.ApiKeyConfigurationName = KeyName));

        // The first client is made when the agent is compiled, as it is for a fixed key; it is not left for the first call.
        var exception = Should.Throw<TraconException>(() => ModelProviderOf(provider).CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.unknownSetting", true)))));

        exception.Message.ShouldContain("anthropic.unknownSetting");
    }

    [Fact]
    public void A_tenant_credential_without_an_address_takes_the_setup_address_that_is_current_when_its_client_is_built()
    {
        var configuration = Configuration((KeyName, "key-one"), (EndpointName, "https://one.example.test/v1/"));

        using var provider = Build(configuration, builder => builder.UseAnthropic(options =>
        {
            options.ApiKeyConfigurationName = KeyName;
            options.EndpointConfigurationName = EndpointName;
        }));

        var model = ModelProviderOf(provider);

        using var before = model.CreateChatClient(TestData.Binding(), new ModelProviderCredential { ApiKey = "sk-tenant-one" });

        configuration[EndpointName] = "https://two.example.test/v1/";

        using var after = model.CreateChatClient(TestData.Binding(), new ModelProviderCredential { ApiKey = "sk-tenant-two" });

        ProviderUriOf(before).ShouldStartWith("https://one.example.test/");
        ProviderUriOf(after).ShouldStartWith("https://two.example.test/");
    }

    private static string ProviderUriOf(IChatClient chatClient)
        => chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>().ProviderUri!.ToString();

    private static IConfiguration Configuration(params (string Name, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration, Action<ITraconBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        configure(services.AddTracon());

        return services.BuildServiceProvider();
    }

    private static AnthropicModelProvider ModelProviderOf(ServiceProvider provider)
        => provider.GetServices<IModelProvider>().Single().ShouldBeOfType<AnthropicModelProvider>();
}
