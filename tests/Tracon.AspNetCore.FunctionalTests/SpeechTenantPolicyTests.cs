using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every speech path checks the tenant's egress policy before the provider receives
/// the tenant's text or audio.
/// </summary>
/// <remarks>
/// <para>
/// The egress policy was applied to model calls, live voice
/// and image generation, but not to text-to-speech or speech-to-text. A tenant
/// whose policy named only its model provider still had its text sent to the speech
/// provider. Each refusal case asserts the provider's call count stays <c>0</c>.
/// </para>
/// <para>
/// The HTTP paths, the function node and the gate's own failure modes are here; the
/// three agent tools live in <c>Tracon.Voice</c> and are covered in its test project,
/// the conversation socket in <see cref="VoiceConversationTenantPolicyTests"/>.
/// </para>
/// </remarks>
public sealed class SpeechTenantPolicyTests
{
    private const string Provider = "studio-engine";
    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task Speak_refuses_a_speech_provider_the_tenant_policy_forbids()
    {
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await SavePolicyAsync(host, "openai");

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain($"not allowed to call speech provider '{Provider}'");
        engine.Calls.ShouldBe(0, "the tenant's text must not reach a speech provider its policy forbids");
    }

    [Fact]
    public async Task With_RequireEgressPolicy_a_tenant_without_a_policy_is_refused_the_speech_provider()
    {
        // The speech resolver is one of the callers that must read the switch.
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(
            engine,
            configureServices: static services => services.Configure<TraconTenantProviderOptions>(
                static options => options.RequireEgressPolicy = true));

        using var refused = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).ShouldContain($"has no egress policy");
        engine.Calls.ShouldBe(0, "the tenant's text must not reach a provider the tenant has no policy for");

        await SavePolicyAsync(host, Provider);

        using var spoken = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        spoken.StatusCode.ShouldBe(HttpStatusCode.OK);
        engine.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Speak_refuses_the_default_synthesizer_too()
    {
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(engine, registerAsDefault: true);

        await SavePolicyAsync(host, "openai");

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Speak_succeeds_when_the_policy_names_the_provider()
    {
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await SavePolicyAsync(host, "openai", Provider);

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        engine.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_policy_judges_the_provider_name_not_the_registration_name()
    {
        // 'studio' is only what the host called the registration. A policy that
        // names the alias must not let the tenant's text through to the provider
        // the alias stands for.
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await SavePolicyAsync(host, "studio");

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        engine.Calls.ShouldBe(0, "a registration alias passed the tenant's egress policy");
    }

    [Fact]
    public async Task Voices_refuses_a_speech_provider_the_tenant_policy_forbids()
    {
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await SavePolicyAsync(host, "openai");

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices?provider=studio", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_function_node_refuses_a_speech_provider_the_tenant_policy_forbids()
    {
        var engine = new CountingSpeechProvider(Provider);
        var node = new NamedSpeechProviderTests.NarrateNode();

        await using var host = await WorkflowSettingsFixtures.StartAsync(builder =>
        {
            builder.AddSpeechSynthesizer("studio", _ => engine);
            node.Register(builder);
        });

        // The writer agent's own model provider is allowed; only the speech provider is not.
        await SavePolicyAsync(host, "echo");
        await SaveOkAsync(host, "narrated", Chain("""{"provider":"studio"}""", NamedSpeechProviderTests.NarrateNode.Name));

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Failed);
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_policy_saved_after_a_call_stops_the_next_call()
    {
        // Nothing is cached: the next call reads the policy saved a moment ago.
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        using (var first = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await SavePolicyAsync(host, "openai");

        using var second = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        second.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        engine.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_policy_store_fails_the_call_before_the_provider()
    {
        // Fail-closed: a policy that cannot be read is not "no policy".
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(
            engine,
            configureServices: static services => services.AddSingleton<ITenantEgressPolicyStore>(new FailingEgressPolicyStore()));

        var resolver = host.Services.GetRequiredService<ISpeechProviderResolver>();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await resolver.GetSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        var status = await SendAndReadStatusAsync(host);

        status.ShouldNotBe(HttpStatusCode.OK);
        engine.Calls.ShouldBe(0, "a failing policy store let the call through");
    }

    [Fact]
    public async Task A_failing_binding_store_fails_the_call_before_the_provider()
    {
        var engine = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(
            engine,
            configureServices: static services => services.AddSingleton<ITenantProviderBindingStore>(new FailingBindingStore()));

        var resolver = host.Services.GetRequiredService<ISpeechProviderResolver>();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await resolver.GetSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        var status = await SendAndReadStatusAsync(host);

        status.ShouldNotBe(HttpStatusCode.OK);
        engine.Calls.ShouldBe(0);
    }

    /// <summary>
    /// Sends a speak request and returns its status, or <c>500</c> when the test
    /// server rethrows the endpoint's unhandled exception to the client.
    /// </summary>
    private static async Task<HttpStatusCode> SendAndReadStatusAsync(TraconTestHost host)
    {
        try
        {
            using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

            return response.StatusCode;
        }
        catch (InvalidOperationException)
        {
            return HttpStatusCode.InternalServerError;
        }
    }

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(
        CountingSpeechProvider engine,
        bool registerAsDefault = false,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddSpeechSynthesizer("studio", _ => engine),
            configureServices: services =>
            {
                if (registerAsDefault)
                {
                    services.AddSingleton<ISpeechSynthesizer>(engine);
                }

                configureServices?.Invoke(services);
            });

    private sealed class FailingEgressPolicyStore : ITenantEgressPolicyStore
    {
        public ValueTask<TenantEgressPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The policy store is unavailable.");

        public ValueTask<TenantEgressPolicy> UpsertAsync(
            string tenantId,
            IReadOnlyList<string> allowedProviders,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The policy store is unavailable.");

        public ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The policy store is unavailable.");
    }

    private sealed class FailingBindingStore : ITenantProviderBindingStore
    {
        public ValueTask<TenantProviderBinding?> GetAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The binding store is unavailable.");

        public ValueTask<IReadOnlyList<TenantProviderBinding>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The binding store is unavailable.");

        public ValueTask UpsertAsync(TenantProviderBinding binding, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The binding store is unavailable.");

        public ValueTask<bool> DeleteAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The binding store is unavailable.");
    }
}
