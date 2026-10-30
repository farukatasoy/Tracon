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
/// A ring keeps the tenant boundary. A tenant that holds its own provider key gets an
/// agent compiled for each run, and that agent keeps the ring and the tenant's key; another
/// tenant's run never goes through a ring built for the first.
/// </summary>
/// <remarks>
/// The tenant's key moves the agent off the compiled-agent cache, so the rings are built
/// again on that path. A test that runs one tenant only would not see a ring built for the wrong
/// tenant, and a test without a tenant key would not see the uncached path.
/// </remarks>
public sealed class ChatClientRingTenantTests
{
    private const string ProviderName = "ring-byok";
    private const string ModelName = "byok-1";
    private const string AgentName = "tenant-ring-agent";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string KeyName = "Tracon:ProviderKeys:tenant-a:RingByok";
    private const string TenantKey = "tenant-a-key";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task Each_tenants_run_goes_through_its_own_ring_and_the_tenant_with_a_key_keeps_it()
    {
        var log = new RingLog();
        var provider = new KeyRecordingProvider(ProviderName);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgentExtension("tenant-ring", context =>
                {
                    var tenant = context.TenantId;
                    context.UseChatClient(ChatClientSlot.ModelCall, inner => new RecordingChatClientRing(inner, $"model-call:{tenant}", log));
                })
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                })
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    ExtensionNames = ["tenant-ring"],
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, TenantKey)]).Build()));

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "tenant-a",
                ProviderName = ProviderName,
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        foreach (var tenant in new[] { "tenant-a", "tenant-b", "tenant-a", "tenant-b" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
            {
                Content = JsonContent.Create(new AgentRunRequest { Message = $"hi from {tenant}" }),
            };
            request.Headers.Add(TenantHeader, tenant);

            using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            var last = log.Calls[^1];
            last.Ring.ShouldBe($"model-call:{tenant}");
            last.Texts[^1].ShouldBe($"hi from {tenant}");
        }

        log.Calls.Select(static call => call.Ring).ShouldBe(
            ["model-call:tenant-a", "model-call:tenant-b", "model-call:tenant-a", "model-call:tenant-b"]);

        // The tenant with a key compiled on each run, with its key; the other tenant's agent was
        // compiled once, with the setup key, and served from the cache.
        provider.Keys.ShouldBe([TenantKey, KeyRecordingProvider.SetupKey, TenantKey]);
    }

    /// <summary>A scripted provider that records the key each chat client was built with.</summary>
    private sealed class KeyRecordingProvider(string name) : ITenantCredentialModelProvider
    {
        public const string SetupKey = "setup-key";

        private readonly FakeModelProvider _inner = new FakeModelProvider(name).EchoesUserMessage();
        private readonly List<string> _keys = [];

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        public IReadOnlyList<string> Keys
        {
            get
            {
                lock (_keys)
                {
                    return [.. _keys];
                }
            }
        }

        public IChatClient CreateChatClient(ModelBinding binding)
        {
            Record(SetupKey);
            return _inner.CreateChatClient(binding);
        }

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential)
        {
            ArgumentNullException.ThrowIfNull(credential);
            Record(credential.ApiKey);
            return _inner.CreateChatClient(binding, credential);
        }

        private void Record(string key)
        {
            lock (_keys)
            {
                _keys.Add(key);
            }
        }
    }
}
