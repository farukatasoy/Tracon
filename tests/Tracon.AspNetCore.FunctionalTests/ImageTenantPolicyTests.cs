using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Image generation passes the tenant's provider checks before the prompt leaves
/// the process: the egress policy first, then the tenant's own credential.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-384: <c>generate_image</c> and <c>POST /api/images/generate</c> called
/// the setup-wide generator and read neither. A tenant whose policy forbade the
/// image provider still had its prompts sent there, and a tenant with its own
/// key for that provider was billed to the host's account. K-467 and K-874 say
/// every path to a provider passes the gate; K-878 is the live-voice precedent
/// this follows: the policy refuses with <c>403</c>, and a tenant binding the
/// generator cannot use refuses too instead of falling back to the setup key.
/// </para>
/// <para>
/// Both public paths are asserted, through the real host: the gate lives in the
/// resolver they share, and a test of one path says nothing about the other.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class ImageTenantPolicyTests
{
    private const string ImageProvider = "test-images";
    private const string KeyName = "Tracon:ProviderKeys:TestImages";
    private static readonly Uri GenerateUri = new("/tracon/api/images/generate", UriKind.Relative);

    [Fact]
    public async Task The_endpoint_refuses_an_image_provider_the_tenant_policy_forbids()
    {
        var generator = new CountingImageGenerator();
        await using var host = await StartAsync(generator);

        await SavePolicyAsync(host, "anthropic");

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain($"'{ImageProvider}'");
        generator.Calls.ShouldBe(0, "the tenant's prompt must not reach a provider its policy forbids");
    }

    [Fact]
    public async Task The_endpoint_generates_when_the_policy_names_the_image_provider()
    {
        var generator = new CountingImageGenerator();
        await using var host = await StartAsync(generator);

        await SavePolicyAsync(host, "anthropic", ImageProvider);

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        generator.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_endpoint_refuses_a_tenant_with_its_own_key_for_the_image_provider()
    {
        // The generator was built with the setup key and cannot use the
        // tenant's. Generating anyway bills the host for the tenant's images.
        var generator = new CountingImageGenerator();
        await using var host = await StartAsync(
            generator,
            services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "tenant-image-key")]).Build()));

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = ImageProvider,
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_agent_tool_refuses_an_image_provider_the_tenant_policy_forbids()
    {
        var generator = new CountingImageGenerator();
        await using var host = await StartAsync(
            generator,
            configureTracon: static builder => builder
                .AddModelProvider(new FakeModelProvider("image-model")
                    .CallsTool("generate_image", new { prompt = "a teal lighthouse" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = "image-agent",
                    Instructions = "Generate the requested image.",
                    Model = new ModelBinding { Provider = "image-model", Model = "image-1" },
                    ToolNames = ["generate_image"],
                }));

        // The agent's own model provider is allowed; only the image provider is not.
        await SavePolicyAsync(host, "image-model");

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/image-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "generate an image" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var runs = host.Services.GetRequiredService<IRunStore>();
        var run = (await runs.QueryRunsAsync(new RunQuery { AgentName = "image-agent" }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        var invocation = (await runs.ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        invocation.ToolName.ShouldBe("generate_image");
        invocation.Succeeded.ShouldBeFalse();
        invocation.Error.ShouldNotBeNull().ShouldContain($"'{ImageProvider}'");
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_agent_tool_refuses_a_tenant_with_its_own_key_for_the_image_provider()
    {
        var generator = new CountingImageGenerator();
        await using var host = await StartAsync(
            generator,
            services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "tenant-image-key")]).Build()),
            configureTracon: static builder => builder
                .AddModelProvider(new FakeModelProvider("image-model")
                    .CallsTool("generate_image", new { prompt = "a teal lighthouse" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = "image-agent",
                    Instructions = "Generate the requested image.",
                    Model = new ModelBinding { Provider = "image-model", Model = "image-1" },
                    ToolNames = ["generate_image"],
                }));

        await SaveBindingAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/image-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "generate an image" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var runs = host.Services.GetRequiredService<IRunStore>();
        var run = (await runs.QueryRunsAsync(new RunQuery { AgentName = "image-agent" }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        var invocation = (await runs.ListToolInvocationsAsync(run.Id, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        invocation.Succeeded.ShouldBeFalse();
        invocation.Error.ShouldNotBeNull().ShouldContain("own credential");
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_endpoint_refuses_a_binding_whose_key_has_no_value()
    {
        // The binding names a configuration key that is not set. The call does
        // not fall back to the setup key: that would bill the host's account.
        var generator = new CountingImageGenerator();
        await using var host = await StartAsync(generator);

        await SaveBindingAsync(host);

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain(KeyName);
        generator.Calls.ShouldBe(0);
    }

    private static Task SaveBindingAsync(TraconTestHost host)
        => host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = ImageProvider,
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken).AsTask();

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(
        CountingImageGenerator generator,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null)
        => TraconTestHost.StartAsync(
            configureTracon: configureTracon,
            configureServices: services =>
            {
                services.Configure<TraconImageOptions>(options =>
                {
                    options.Enabled = true;
                    options.Provider = ImageProvider;
                    options.Model = "image-1";
                });
                services.AddKeyedSingleton<IImageGenerator>(ImageProvider, generator);
                configureServices?.Invoke(services);
            });

    private sealed class CountingImageGenerator : IImageGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];

            return Task.FromResult(new ImageGenerationResponse([new DataContent(png, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore MEAI001
