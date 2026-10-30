using Microsoft.Extensions.AI;
using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// The factory's deployment resolution, credential selection, pipeline
/// setup, and rejection of provider settings.
/// </summary>
public sealed class AzureOpenAIChatClientFactoryTests
{
    [Fact]
    public void Factory_returns_a_RAW_client_and_does_not_build_a_pipeline()
    {
        // The tool-call loop and telemetry moved to
        // ModelProviderRegistry. If the factory built them too, two nested
        // FunctionInvokingChatClient instances would form, and the content
        // guard the registry adds would stay OUTSIDE the loop — tool
        // results would never be inspected.
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldBeNull();
    }

    [Fact]
    public void Deployment_name_is_reflected_in_client_metadata_as_the_model()
    {
        // On Azure's v1 endpoint the deployment name is the request's `model`
        // field; in MEAI metadata the same name fills the "model" field.
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Deployment);
        metadata.ProviderUri!.ToString().ShouldStartWith(TestData.EndpointText);
    }

    [Fact]
    public void Default_deployment_is_used_when_deployment_is_empty()
    {
        using var chatClient = Factory(options => options.DefaultDeployment = TestData.Deployment)
            .CreateChatClient(new ModelBinding { Provider = AzureOpenAIProviderNames.AzureOpenAI, Model = "  " });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Deployment);
    }

    [Fact]
    public void Error_says_it_expected_a_deployment_not_a_model_when_neither_is_given()
    {
        var exception = Should.Throw<TraconException>(() => Factory()
            .CreateChatClient(new ModelBinding { Provider = AzureOpenAIProviderNames.AzureOpenAI, Model = " " }));

        // This is the one big source of confusion this change carries; if
        // the message doesn't say so explicitly, the user assumes "model
        // not found" and looks in the wrong place.
        exception.Message.ShouldContain("DEPLOYMENT");
        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.DefaultDeployment));
    }

    [Theory]
    [InlineData("https://r.openai.azure.com", "https://r.openai.azure.com/openai/v1/")]
    [InlineData("https://r.openai.azure.com/OpenAI/V1", "https://r.openai.azure.com/OpenAI/V1/")]
    [InlineData("https://r.openai.azure.com/?x=1#frag", "https://r.openai.azure.com/openai/v1/")]
    [InlineData("https://gw.example/azure/", "https://gw.example/azure/openai/v1/")]
    public void The_v1_address_is_built_from_the_path_only(string address, string expected)
        => AzureOpenAIChatClientFactory.V1Endpoint(new Uri(address)).AbsoluteUri.ShouldBe(expected);

    [Fact]
    public void Client_setup_without_an_endpoint_fails()
    {
        var exception = Should.Throw<TraconException>(
            () => AzureOpenAIChatClientFactory.CreateClient(new AzureOpenAIProviderOptions { ApiKey = TestData.ApiKey }));

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Client_setup_without_credentials_explains_both_paths()
    {
        var exception = Should.Throw<TraconException>(
            () => AzureOpenAIChatClientFactory.CreateClient(new AzureOpenAIProviderOptions { Endpoint = TestData.Endpoint }));

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.ApiKey));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void Credential_factory_is_called_once_during_setup()
    {
        var credential = new FakeTokenCredential();
        var callCount = 0;

        var factory = new AzureOpenAIChatClientFactory(TestData.Options(o =>
        {
            o.ApiKey = null;
            o.CredentialFactory = () => { callCount++; return credential; };
        }));

        using var first = factory.CreateChatClient(TestData.Binding());
        using var second = factory.CreateChatClient(TestData.Binding("another-deployment"));

        // The client is built once; generating a new credential on every
        // compile would waste the token cache.
        callCount.ShouldBe(1);
    }

    [Fact]
    public void Credential_factory_overrides_the_key()
    {
        // When both are given, the more secure one wins; silently falling
        // back to the key is not the behavior the user expects.
        var credential = new FakeTokenCredential();
        var used = false;

        var client = AzureOpenAIChatClientFactory.CreateClient(TestData.Options(o =>
            o.CredentialFactory = () => { used = true; return credential; }));

        used.ShouldBeTrue();
        client.ShouldNotBeNull();
    }

    [Fact]
    public void Credential_factory_returning_null_gives_a_clear_error()
    {
        var exception = Should.Throw<TraconException>(
            () => AzureOpenAIChatClientFactory.CreateClient(TestData.Options(o =>
            {
                o.ApiKey = null;
                o.CredentialFactory = static () => null!;
            })));

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void Only_the_strict_schema_setting_is_supported()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("azure-openai.noSuchSetting", true)))));

        exception.Message.ShouldContain("are not recognized");
        exception.Message.ShouldContain($"Supported keys: {AzureOpenAIProviderNames.StrictJsonSchemaSetting}");
    }

    [Fact]
    public void The_strict_schema_setting_is_accepted()
    {
        using var client = Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((AzureOpenAIProviderNames.StrictJsonSchemaSetting, true))));

        client.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_strict_schema_setting_wraps_the_client_only_when_it_is_on(bool strict)
    {
        using var client = Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((AzureOpenAIProviderNames.StrictJsonSchemaSetting, strict))));

        (client is OpenAIStrictResponseSchemaChatClient).ShouldBe(strict);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    public void A_strict_schema_value_that_is_not_a_boolean_is_rejected_without_repeating_the_value(string value)
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings((AzureOpenAIProviderNames.StrictJsonSchemaSetting, value)))));

        exception.Message.ShouldContain(AzureOpenAIProviderNames.StrictJsonSchemaSetting);
        exception.Message.ShouldContain("boolean");
        exception.Message.ShouldNotContain($"'{value}'");
    }

    [Fact]
    public void Setting_belonging_to_another_provider_gives_a_separate_error_message()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.promptCaching", true)))));

        // A wrong prefix means "you wrote the setting for another
        // provider"; the fix for that differs from an unrecognized key.
        exception.Message.ShouldContain("do not belong");
        exception.Message.ShouldContain("anthropic.promptCaching");
    }

    [Fact]
    public void Connection_without_settings_works_fine()
    {
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        chatClient.ShouldNotBeNull();
    }

    private static AzureOpenAIChatClientFactory Factory(Action<AzureOpenAIProviderOptions>? configure = null)
        => new(TestData.Options(configure));
}
