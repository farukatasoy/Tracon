using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as AgentExtensionRunTests.cs.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant that rotates its own provider key, through the configuration the binding names, is served with the new key on
/// the next run, and a rotation of one tenant changes nothing for another tenant that holds the same key value.
/// </summary>
/// <remarks>
/// <para>
/// The client of a tenant binding is kept under the binding and replaced when its key changes. Crosses the boundaries a
/// unit test cannot: the tenant comes from the HTTP header, the binding from the store, the key from the configuration
/// at the moment of the call, and the provider is the real OpenAI-compatible client in front of a local HTTP server, so
/// the key that counts is the one on the wire.
/// </para>
/// <para>
/// The second test pins the wiring itself: what the registry hands a provider is a credential that names the tenant and
/// the provider of its binding, and a call with no binding hands it none.
/// </para>
/// </remarks>
public sealed class TenantProviderKeyRotationTests
{
    private const string Provider = "rotating-key";
    private const string Model = "fake-local-model";
    private const string AgentName = "rotating-key-agent";
    private const string TenantHeader = "X-Tracon-Tenant";

    [Fact]
    public async Task A_rotated_key_is_the_key_of_the_next_run_and_another_tenants_client_is_left_alone()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new(KeyName("tenant-a"), "key-one"),

                // The same value as tenant A's first key: a client built for A must never be handed to B.
                new(KeyName("tenant-b"), "key-one"),
            ])
            .Build();

        await using var host = await StartAsync(server, configuration);
        await BindAsync(host, "tenant-a", server);
        await BindAsync(host, "tenant-b", server);

        await RunAsync(host, "tenant-a");
        await RunAsync(host, "tenant-b");

        configuration[KeyName("tenant-a")] = "key-two";

        await RunAsync(host, "tenant-a");
        await RunAsync(host, "tenant-b");

        configuration[KeyName("tenant-a")] = "key-three";

        await RunAsync(host, "tenant-a");
        await RunAsync(host, "tenant-a");
        await RunAsync(host, "tenant-b");

        // Every run was sent with the key its tenant held at that moment: after a rotation the old key is never sent again,
        // and tenant B kept its own key throughout.
        server.ReceivedAuthorizationHeaders.ShouldBe(
        [
            "Bearer key-one",
            "Bearer key-one",
            "Bearer key-two",
            "Bearer key-one",
            "Bearer key-three",
            "Bearer key-three",
            "Bearer key-one",
        ]);
    }

    [Fact]
    public async Task The_credential_a_provider_receives_names_the_tenant_and_the_provider_of_its_binding()
    {
        var provider = new ScopeRecordingProvider(Provider);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection([new(KeyName("tenant-a"), "key-a"), new(KeyName("tenant-b"), "key-b")])
                    .Build()));

        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
                new TenantProviderBinding
                {
                    TenantId = tenant,
                    ProviderName = Provider,
                    ApiKeyConfigurationName = KeyName(tenant),
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                TestContext.Current.CancellationToken);
        }

        foreach (var tenant in new[] { "tenant-a", "tenant-b", "tenant-without-a-binding" })
        {
            using var request = Post(tenant);
            using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        // A tenant with no binding is served by the key the provider was set up with, and no credential names a binding.
        provider.Scopes.ShouldBe(["tenant-a/rotating-key", "tenant-b/rotating-key", null]);
    }

    private static string KeyName(string tenant) => $"Tracon:ProviderKeys:{tenant}:RotatingKey";

    private static Task<TraconTestHost> StartAsync(FakeOpenAiCompatibleServer server, IConfiguration configuration)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .UseOpenAICompatible(Provider, options => options.Endpoint = server.BaseAddress)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = Provider, Model = Model },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services =>
            {
                services.AddSingleton(configuration);

                // The server is on the loopback interface; the endpoint is the tenant's own, so the guard judges it.
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
            });

    private static async Task BindAsync(TraconTestHost host, string tenant, FakeOpenAiCompatibleServer server)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/tracon/api/tenants/{tenant}/providers/{Provider}", UriKind.Relative))
        {
            Content = JsonContent.Create(new { apiKeyConfigurationName = KeyName(tenant), endpoint = server.BaseAddress.ToString() }),
        };
        request.Headers.Add(TenantHeader, tenant);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task RunAsync(TraconTestHost host, string tenant)
    {
        using var request = Post(tenant);
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static HttpRequestMessage Post(string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = $"hello from {tenant}", SessionId = Guid.NewGuid().ToString("N") }),
        };

        // The Idempotency-Key header selects the single JSON response.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add(TenantHeader, tenant);

        return request;
    }

    /// <summary>A scripted provider that records the binding name each tenant credential carried.</summary>
    private sealed class ScopeRecordingProvider(string name) : ITenantCredentialModelProvider
    {
        private readonly FakeModelProvider _inner = new FakeModelProvider(name).EchoesUserMessage();
        private readonly List<string?> _scopes = [];

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = Model }];

        public IReadOnlyList<string?> Scopes
        {
            get
            {
                lock (_scopes)
                {
                    return [.. _scopes];
                }
            }
        }

        public IChatClient CreateChatClient(ModelBinding binding)
        {
            Record(null);

            return _inner.CreateChatClient(binding);
        }

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);
            Record(TenantCredentialScope.Of(credential));

            return _inner.CreateChatClient(binding, credential);
        }

        private void Record(string? scope)
        {
            lock (_scopes)
            {
                _scopes.Add(scope);
            }
        }
    }
}
