using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An endpoint a tenant binds to a provider, named by a host NAME that resolves to the loopback interface, reaches no
/// listener through any of the provider clients, and the name is looked up through the guard.
/// </summary>
/// <remarks>
/// <para>
/// The binding endpoint judges an IP literal when it is saved and leaves a name to the guard at connection time, because
/// a name can answer differently later. So the guard is the whole defence for every name, and the question is whether
/// each provider's client was built with it: a provider factory that forgot to pass the guard dials the tenant's name
/// unchecked, which is how the OpenAI-compatible provider once shipped.
/// </para>
/// <para>
/// The services are the real ones, built the way a consumer builds them, with one difference: the guard in the container
/// is the one this test constructs, with a resolver that answers every name with the loopback address of a real
/// listener and counts the questions. A client that was handed no guard never asks the resolver, so the count shows
/// the guard was in the path, and the listener shows nothing got through.
/// </para>
/// </remarks>
public sealed class TenantEndpointHostNameTests
{
    [Fact]
    public async Task No_provider_client_dials_a_tenant_name_that_resolves_to_loopback()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var asked = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        var resolver = new EgressHostResolver((host, _) =>
        {
            asked.AddOrUpdate(host, 1, static (_, count) => count + 1);

            return ValueTask.FromResult<IPAddress[]>([IPAddress.Loopback]);
        });

        var services = new ServiceCollection();

        // Registered before the product's own, which is a TryAdd and so yields to it.
        services.AddSingleton(new EgressSocketGuard(static () => EgressAddressPolicy.Deny, resolver));
        services.AddTracon()
            .UseOpenAI("setup-key-not-real")
            .UseOpenAICompatible("compatible", static options =>
            {
                options.Endpoint = new Uri("https://compatible.example.com/v1");
                options.ApiKey = "setup-key-not-real";
            })
            .UseAzureOpenAI(new Uri("https://setup.example.openai.azure.com/"), "setup-key-not-real", static options => options.DefaultDeployment = "deployment")
            .UseAnthropic("setup-key-not-real")
            .UseGoogle("setup-key-not-real");

        await using var provider = services.BuildServiceProvider();

        var tenantProviders = provider.GetServices<IModelProvider>()
            .OfType<ITenantCredentialModelProvider>()
            .ToList();

        // Every provider the registrations above produced, or the sweep covers less than it says.
        tenantProviders.Select(static candidate => candidate.Name).ShouldBe(
            ["openai", "openai-responses", "compatible", "azure-openai", "anthropic", "google"],
            ignoreOrder: true);

        // Concurrently: an SDK retries a refused connection with a back-off of its own, and the providers need not wait
        // for one another. Each gets a name of its own, so the guard's questions can be told apart.
        await Task.WhenAll(tenantProviders.Select(async candidate =>
        {
            var host = $"{candidate.Name}.tenant-chosen.example";
            var binding = new ModelBinding { Provider = candidate.Name, Model = candidate.Models.Count > 0 ? candidate.Models[0].Name : "model" };

            using var chat = candidate.CreateChatClient(
                binding,
                new ModelProviderCredential { ApiKey = "tenant-key-not-real", Endpoint = $"http://{host}:{port}/v1" });

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));

            await Should.ThrowAsync<Exception>(
                async () => await chat.GetResponseAsync("hello", cancellationToken: deadline.Token),
                $"the {candidate.Name} client did not fail");

            asked.GetValueOrDefault(host).ShouldBeGreaterThan(0, $"the {candidate.Name} client did not ask the guard to resolve the tenant's name");
        }));

        listener.Pending().ShouldBeFalse("a provider client opened a connection to the loopback listener");
    }
}
