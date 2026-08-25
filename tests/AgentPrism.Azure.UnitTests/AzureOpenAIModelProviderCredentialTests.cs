using AgentPrism.Azure.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.Azure.UnitTests;

/// <summary>
/// Verifies <see cref="AzureOpenAIModelProvider"/>'s per-tenant credential
/// handling (phase 65, BYOK). Unlike the other three providers, Azure has no
/// single global address — a tenant may override only the key and keep
/// pointing at the setup-time resource, or override both.
/// </summary>
public sealed class AzureOpenAIModelProviderCredentialTests
{
    private static readonly Uri TenantEndpoint = new("https://tenant-resource.openai.azure.com/");

    private static readonly IReadOnlyList<ModelDescriptor> TestCatalog =
    [
        new ModelDescriptor { Name = TestData.Deployment },
    ];

    [Fact]
    public void Null_credential_uses_the_global_setup_time_client()
    {
        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(TestData.EndpointText);
    }

    [Fact]
    public void Tenant_credential_endpoint_overrides_the_global_resource()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant", Endpoint = TenantEndpoint.ToString() };

        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(TenantEndpoint.ToString());
    }

    [Fact]
    public void Tenant_credential_without_an_endpoint_keeps_using_the_global_resource()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant" };

        using var chatClient = CreateProvider().CreateChatClient(TestData.Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(TestData.EndpointText);
    }

    private static AzureOpenAIModelProvider CreateProvider()
    {
        var options = TestData.Options();

        return new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(options),
            TestCatalog,
            healthCheckOptions: options);
    }
}
