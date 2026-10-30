using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// A tenant-supplied endpoint on a provider registered with
/// <c>UseOpenAICompatible()</c> goes through the outbound address guard, as it
/// does on every other provider.
/// </summary>
/// <remarks>
/// 🚨 An earlier fix class sweep (2026-10-13, independent review): the
/// <c>UseOpenAICompatible()</c> factory built <see cref="OpenAIModelProvider"/>
/// without <c>egressGuard</c>, while the OpenAI, Azure, Anthropic and Google
/// factories pass it. <c>GuardFor</c> then returned <see langword="null"/> and a
/// tenant binding's endpoint was dialled unchecked. The binding save path checks
/// only IP literals and leaves host names to this guard, so a host name that
/// resolves to a private or metadata address received the tenant's prompts and
/// key. The provider has to come from DI: a provider built by hand in a test
/// passes whatever guard the test gives it.
/// </remarks>
public sealed class OpenAICompatibleTenantEndpointGuardTests
{
    [Fact]
    public async Task A_tenant_endpoint_on_a_compatible_provider_is_checked_by_the_egress_guard()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseOpenAICompatible("openrouter", static options =>
        {
            options.Endpoint = new Uri("https://openrouter.example.com/api/v1");
            options.ApiKey = "setup-key-not-real";
        });

        await using var provider = services.BuildServiceProvider();

        var compatible = provider.GetServices<IModelProvider>()
            .Single(static candidate => string.Equals(candidate.Name, "openrouter", StringComparison.Ordinal))
            .ShouldBeAssignableTo<ITenantCredentialModelProvider>();

        // A loopback IP literal stands in for a host name that resolves to a
        // private address: the guard decides on the resolved address either way.
        using var chatClient = compatible.CreateChatClient(
            new ModelBinding { Provider = "openrouter", Model = "openai/gpt-5.4-mini" },
            new ModelProviderCredential { ApiKey = "tenant-key-not-real", Endpoint = "https://127.0.0.1:9/v1" });

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
}
