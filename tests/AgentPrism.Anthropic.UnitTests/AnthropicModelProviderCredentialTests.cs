using AgentPrism.Anthropic.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.Anthropic.UnitTests;

/// <summary>
/// Verifies <see cref="AnthropicModelProvider"/>'s per-tenant credential
/// handling (phase 65, BYOK). See <c>OpenAIModelProviderCredentialTests</c>
/// for the shared rationale.
/// </summary>
public sealed class AnthropicModelProviderCredentialTests
{
    private static readonly Uri GlobalEndpoint = new("https://global.example.com/v1");
    private static readonly Uri TenantEndpoint = new("https://tenant.example.com/v1");

    private static readonly IReadOnlyList<ModelDescriptor> TestCatalog =
    [
        new ModelDescriptor { Name = TestData.Model },
    ];

    [Fact]
    public void Null_credential_uses_the_global_setup_time_client()
    {
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential: null);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(GlobalEndpoint.ToString());
    }

    [Fact]
    public void Tenant_credential_key_and_endpoint_are_reflected_in_the_produced_client()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant", Endpoint = TenantEndpoint.ToString() };

        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(TenantEndpoint.ToString());
    }

    [Fact]
    public void Tenant_credential_without_an_endpoint_falls_back_to_the_global_endpoint()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant" };

        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(GlobalEndpoint.ToString());
    }

    private static AnthropicModelProvider CreateProvider()
    {
        var options = TestData.Options(o => o.Endpoint = GlobalEndpoint);

        return new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(options),
            TestCatalog,
            healthCheckOptions: options);
    }
}
