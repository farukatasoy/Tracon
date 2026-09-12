using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>
/// Verifies <see cref="GoogleModelProvider"/>'s per-tenant credential handling
/// (phase 65, BYOK). See <c>OpenAIModelProviderCredentialTests</c> for the
/// shared rationale.
/// </summary>
/// <remarks>
/// 🚨 Measured: unlike the other three providers, the Google GenAI SDK's
/// <c>Client.AsIChatClient()</c> does NOT reflect a custom
/// <c>HttpOptions.BaseUrl</c> in <c>ChatClientMetadata.ProviderUri</c> — it
/// always reports the fixed default Google endpoint, even when a different
/// base address was given at client construction. These tests therefore
/// cannot prove the endpoint override through <c>ProviderUri</c> the way the
/// sibling packages do; they prove the credential path is exercised (a
/// separate, working client is produced, no exception) instead.
/// </remarks>
public sealed class GoogleModelProviderCredentialTests
{
    private static readonly Uri GlobalEndpoint = new("https://global.example.com/");

    private static readonly IReadOnlyList<ModelDescriptor> TestCatalog =
    [
        new ModelDescriptor { Name = TestData.Model },
    ];

    [Fact]
    public void Null_credential_produces_a_working_client()
    {
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Tenant_credential_with_its_own_key_and_endpoint_produces_a_working_client()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant", Endpoint = "https://tenant.example.com/" };

        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Tenant_credential_without_an_endpoint_produces_a_working_client()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant" };

        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential);

        chatClient.ShouldNotBeNull();
    }

    private static GoogleModelProvider CreateProvider()
    {
        var options = TestData.Options(o => o.Endpoint = GlobalEndpoint);

        return new GoogleModelProvider(
            GoogleProviderNames.Google,
            new GoogleChatClientFactory(options),
            TestCatalog,
            healthCheckOptions: options);
    }
}
