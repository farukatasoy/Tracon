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
        // 🚨 Phase 48: the tool-call loop and telemetry moved to
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
        // In Azure the path is shaped like
        // {endpoint}/openai/deployments/{deployment}/chat/completions; in
        // MEAI metadata this name fills the "model" field.
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

        // This is the one big source of confusion this phase carries; if
        // the message doesn't say so explicitly, the user assumes "model
        // not found" and looks in the wrong place.
        exception.Message.ShouldContain("DEPLOYMENT");
        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.DefaultDeployment));
    }

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
    public void Sovereign_cloud_audience_can_be_given_to_the_client()
    {
        // Setup must not fail; the audience goes into the SDK's internal
        // configuration and cannot be read from outside. Its counterpart
        // on the health-check side is tested separately.
        var client = AzureOpenAIChatClientFactory.CreateClient(TestData.Options(o =>
            o.Audience = "https://cognitiveservices.azure.us/.default"));

        client.ShouldNotBeNull();
    }

    [Fact]
    public void No_provider_setting_is_supported()
    {
        var exception = Should.Throw<TraconException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("azure-openai.noSuchSetting", true)))));

        exception.Message.ShouldContain("are not recognized");
        exception.Message.ShouldContain("supports no extra settings");
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
