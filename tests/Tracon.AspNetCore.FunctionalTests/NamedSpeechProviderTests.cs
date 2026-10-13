using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Several speech synthesizers live in one process, and a caller selects one by name:
/// <c>POST /api/voice/speak</c>, <c>GET /api/voice/voices</c> and a workflow function
/// node reach the instance the name registered, never another.
/// </summary>
/// <remarks>
/// F-298: a process knew one unnamed provider with one process-wide key. A host that
/// picks the text-to-speech engine per tenant or per pipeline (a hosted service, a
/// local GPU model) had no way to name the engine. Each case counts the calls of every
/// registered instance, so a name that reaches the wrong one is red even when the
/// response is <c>200</c>.
/// </remarks>
public sealed class NamedSpeechProviderTests
{
    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task Speak_calls_the_synthesizer_the_request_names()
    {
        var engines = new Engines();
        await using var host = await StartAsync(engines);

        using (var studio = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" }))
        {
            studio.StatusCode.ShouldBe(HttpStatusCode.OK, await studio.Content.ReadAsStringAsync());
        }

        // Names are compared in lower case.
        using (var gpu = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "GPU" }))
        {
            gpu.StatusCode.ShouldBe(HttpStatusCode.OK, await gpu.Content.ReadAsStringAsync());
        }

        using (var unnamed = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello" }))
        {
            unnamed.StatusCode.ShouldBe(HttpStatusCode.OK, await unnamed.Content.ReadAsStringAsync());
        }

        engines.Studio.Calls.ShouldBe(1);
        engines.Gpu.Calls.ShouldBe(1);
        engines.Default.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Voices_come_from_the_synthesizer_the_query_names()
    {
        var engines = new Engines();
        await using var host = await StartAsync(engines);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices?provider=gpu", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response))[0].GetProperty("voiceId").GetString().ShouldBe("gpu-engine-voice");

        engines.Gpu.Calls.ShouldBe(1);
        engines.Studio.Calls.ShouldBe(0);
        engines.Default.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Providers_lists_the_default_and_every_named_provider_without_calling_them()
    {
        var engines = new Engines();
        await using var host = await StartAsync(engines);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/providers", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Select(static entry => (
                Name: entry.GetProperty("name").GetString(),
                Kind: entry.GetProperty("kind").GetString(),
                Provider: entry.GetProperty("providerName").GetString(),
                Credentials: entry.GetProperty("supportsTenantCredentials").GetBoolean()))
            .ToList();

        listed.ShouldBe(
        [
            (null, "Synthesizer", "default-engine", false),
            ("gpu", "Synthesizer", "gpu-engine", false),
            ("studio", "Synthesizer", "studio-engine", true),
            (null, "Transcriber", "default-engine", false),
            ("studio", "Transcriber", "studio-engine", true),
        ]);

        engines.Studio.Calls.ShouldBe(0);
        engines.Gpu.Calls.ShouldBe(0);
        engines.Default.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Two_names_for_one_instance_resolve_to_that_instance()
    {
        // One account shared by two names: one instance, so one concurrency counter.
        var shared = new CountingSpeechProvider("shared-engine");

        await using var host = await TraconTestHost.StartAsync(configureTracon: builder => builder
            .AddSpeechSynthesizer("first", _ => shared)
            .AddSpeechSynthesizer("second", _ => shared));

        var resolver = host.Services.GetRequiredService<ISpeechProviderResolver>();

        var first = await resolver.GetSynthesizerAsync("first", TestContext.Current.CancellationToken);
        var second = await resolver.GetSynthesizerAsync("second", TestContext.Current.CancellationToken);

        first.ShouldBeSameAs(shared);
        second.ShouldBeSameAs(shared);
    }

    [Fact]
    public async Task A_function_node_speaks_with_the_synthesizer_its_settings_name()
    {
        var engines = new Engines();
        var node = new NarrateNode();

        await using var host = await StartWorkflowAsync(engines, node);
        await SaveOkAsync(host, "narrated", Chain("""{"provider":"studio"}""", NarrateNode.Name));

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Output.ShouldBe("studio-engine:14");
        engines.Studio.Calls.ShouldBe(1);
        engines.Default.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_cancelled_resolution_never_reaches_the_provider()
    {
        // The policy read is the slow step: cancelling while it waits must end the
        // call there, not after the provider received the text.
        var engines = new Engines();
        var policies = new BlockingEgressPolicyStore();

        await using var host = await StartAsync(
            engines,
            services => services.AddSingleton<ITenantEgressPolicyStore>(policies));

        var resolver = host.Services.GetRequiredService<ISpeechProviderResolver>();

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var resolution = resolver.GetSynthesizerAsync("studio", cancellation.Token).AsTask();

        await policies.Entered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(resolution);
        engines.Studio.Calls.ShouldBe(0);

        // A token cancelled before the call does not even read the policy.
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await resolver.GetSynthesizerAsync("studio", cancellation.Token));
        policies.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_function_node_is_not_retried()
    {
        // A refusal is the tenant's rule, not a provider fault: a retry cannot change
        // it, so the node's retry policy must not run the handler again.
        var engines = new Engines();
        var node = new NarrateNode();

        await using var host = await StartWorkflowAsync(engines, node);
        await SavePolicyAsync(host, "echo");
        await SaveOkAsync(host, "narrated", Chain("""{"provider":"studio"}""", NarrateNode.Name));

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Failed);
        node.Attempts.ShouldBe(1, "a refused speech provider was retried as if it were a transient fault");
        engines.Studio.Calls.ShouldBe(0);
    }

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(Engines engines, Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: engines.Register,
            configureServices: services =>
            {
                services.AddSingleton<ISpeechSynthesizer>(engines.Default);
                services.AddSingleton<ISpeechTranscriber>(engines.Default);
                configureServices?.Invoke(services);
            });

    private static Task<TraconTestHost> StartWorkflowAsync(Engines engines, NarrateNode node)
        => WorkflowSettingsFixtures.StartAsync(builder =>
        {
            builder.Services.AddSingleton<ISpeechSynthesizer>(engines.Default);
            engines.Register(builder);
            node.Register(builder);
        });

    /// <summary>The three synthesizers of one host: the default and two named ones.</summary>
    private sealed class Engines
    {
        public CountingSpeechProvider Default { get; } = new("default-engine");

        public CountingSpeechProvider Studio { get; } = new("studio-engine", supportsTenantCredentials: true);

        public CountingSpeechProvider Gpu { get; } = new("gpu-engine");

        public void Register(ITraconBuilder builder)
            => builder
                .AddSpeechSynthesizer("studio", _ => Studio)
                .AddSpeechSynthesizer("gpu", _ => Gpu)
                .AddSpeechTranscriber("studio", _ => Studio);
    }

    /// <summary>A function node that speaks the last message with the synthesizer its settings name.</summary>
    internal sealed class NarrateNode
    {
        public const string Name = "narrate";

        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public void Register(ITraconBuilder builder)
            => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, NarrationSettings>(
                Name,
                provider =>
                {
                    // Taken at setup: the provider a node sees at call time is not the host's.
                    var speech = provider.GetRequiredService<ISpeechProviderResolver>();

                    return async (messages, settings, _, cancellationToken) =>
                    {
                        Interlocked.Increment(ref _attempts);

                        var synthesizer = await speech.GetSynthesizerAsync(settings.Provider, cancellationToken);
                        var audio = await synthesizer.SynthesizeAsync(
                            new SpeechRequest { Text = messages[^1].Text },
                            cancellationToken);

                        return [new ChatMessage(ChatRole.User, $"{synthesizer.ProviderName}:{audio.Data.Length}")];
                    };
                },
                static options => options.RetryPolicy = new WorkflowNodeRetryPolicy
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                });
    }

    /// <summary>The settings of the narrate node.</summary>
    internal sealed record NarrationSettings
    {
        public string? Provider { get; init; }
    }

    /// <summary>An egress policy store whose read waits until it is cancelled.</summary>
    private sealed class BlockingEgressPolicyStore : ITenantEgressPolicyStore
    {
        private int _reads;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads => Volatile.Read(ref _reads);

        public async ValueTask<TenantEgressPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            Entered.TrySetResult();

            await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated

            return null;
        }

        public ValueTask<TenantEgressPolicy> UpsertAsync(
            string tenantId,
            IReadOnlyList<string> allowedProviders,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
