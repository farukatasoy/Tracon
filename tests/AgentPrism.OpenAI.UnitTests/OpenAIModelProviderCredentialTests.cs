using Microsoft.Extensions.AI;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Verifies <see cref="OpenAIModelProvider"/>'s per-tenant credential handling
/// (phase 65, BYOK). Measured: <c>OpenAIClient</c> is built once and shared,
/// so a tenant credential must build (and cache) a SEPARATE client.
/// </summary>
public sealed class OpenAIModelProviderCredentialTests
{
    private static readonly Uri GlobalEndpoint = new("https://global.example.com/v1");
    private static readonly Uri TenantEndpoint = new("https://tenant.example.com/v1");

    private static readonly IReadOnlyList<ModelDescriptor> TestCatalog =
    [
        new ModelDescriptor { Name = "gpt-4o-mini" },
    ];

    [Fact]
    public void Null_credential_uses_the_global_setup_time_client()
    {
        using var chatClient = CreateProvider().CreateChatClient(Binding(), credential: null);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(GlobalEndpoint.ToString());
    }

    [Fact]
    public void Tenant_credential_key_and_endpoint_are_reflected_in_the_produced_client()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant", Endpoint = TenantEndpoint.ToString() };

        using var chatClient = CreateProvider().CreateChatClient(Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(TenantEndpoint.ToString());
    }

    [Fact]
    public void Tenant_credential_without_an_endpoint_falls_back_to_the_global_endpoint()
    {
        var credential = new ModelProviderCredential { ApiKey = "sk-tenant" };

        using var chatClient = CreateProvider().CreateChatClient(Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.ProviderUri!.ToString().ShouldStartWith(GlobalEndpoint.ToString());
    }

    [Fact]
    public void The_key_value_never_appears_in_client_metadata()
    {
        const string secret = "sk-tenant-super-secret-1234567890";
        var credential = new ModelProviderCredential { ApiKey = secret };

        using var chatClient = CreateProvider().CreateChatClient(Binding(), credential);

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        (metadata.ProviderUri?.ToString() ?? string.Empty).ShouldNotContain(secret);
    }

    private static ModelBinding Binding(string model = "gpt-4o-mini")
        => new() { Provider = OpenAIProviderNames.ChatCompletions, Model = model };

    private static OpenAIModelProvider CreateProvider()
    {
        var options = new OpenAIProviderOptions { ApiKey = "sk-global", Endpoint = GlobalEndpoint };

        return new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(options),
            TestCatalog,
            healthCheckOptions: options);
    }
}
