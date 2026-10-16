using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

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

    [Fact]
    public async Task A_tenant_endpoint_is_checked_by_the_egress_guard()
    {
        // The provider must come from DI: one built by hand gets whatever guard
        // the test gives it (see OpenAICompatibleTenantEndpointGuardTests). A
        // loopback IP literal stands in for a host name that resolves to a
        // private address; the guard decides on the resolved address either way.
        var services = new ServiceCollection();
        services.AddTracon().UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey);
        await using var provider = services.BuildServiceProvider();

        var azure = provider.GetServices<IModelProvider>()
            .Single(static candidate => string.Equals(candidate.Name, AzureOpenAIProviderNames.AzureOpenAI, StringComparison.Ordinal))
            .ShouldBeAssignableTo<ITenantCredentialModelProvider>();

        using var chatClient = azure.CreateChatClient(
            TestData.Binding(),
            new ModelProviderCredential { ApiKey = "tenant-key-not-real", Endpoint = "https://127.0.0.1:9/" });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var failure = await Should.ThrowAsync<Exception>(
            async () => await chatClient.GetResponseAsync("hello", cancellationToken: deadline.Token));

        // The guard's refusal names the setting; an unguarded dial fails with a
        // plain connection error instead.
        Describe(failure).ShouldContain(nameof(TraconEgressOptions.AllowPrivateNetworkTargets));
    }

    private static string Describe(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
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
