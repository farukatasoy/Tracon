using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A dialogue passes the same tenant gate as every other speech call: the resolver
/// applies the tenant's egress policy to the dialogue provider's name and sends the
/// tenant's own key only to a provider that can use it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISpeechProviderResolver.GetDialogueSynthesizerAsync"/>
/// is the only way to the capability. The trap was a wrapper that implements
/// only <see cref="ISpeechSynthesizer"/>: a type test on it answered "no dialogue" for a
/// tenant with its own key and "dialogue" for one without. The resolver reads the
/// capability from the registered instance instead, so both tenants get the same answer.
/// </para>
/// <para>
/// Every refusal asserts the provider's call count stays <c>0</c>: a check that answers
/// after the provider already received the turns proves nothing. The workflow function
/// node is the path a host uses (there is no dialogue tool or endpoint).
/// </para>
/// </remarks>
public sealed class SpeechDialogueTenantTests
{
    private const string Provider = "studio-engine";
    private const string FlatKeyName = "Tracon:ProviderKeys:Studio";
    private const string FlatKeyValue = "default-tenant-studio-value";

    [Fact]
    public async Task A_function_node_voices_a_dialogue_with_the_synthesizer_its_settings_name()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        var node = new DialogueNode();

        await using var host = await StartWorkflowAsync(engine, node);
        await SaveOkAsync(host, "conversation", Chain("""{"provider":"studio"}""", DialogueNode.Name));

        var run = await RunAsync(host, "conversation");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Output.ShouldNotBeNull().ShouldStartWith($"{Provider}:2 segments");
        engine.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_function_node_refuses_a_dialogue_provider_the_tenant_policy_forbids_and_is_not_retried()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        var node = new DialogueNode();

        await using var host = await StartWorkflowAsync(engine, node);

        // The writer agent's own model provider is allowed; only the speech provider is not.
        await SavePolicyAsync(host, "echo");
        await SaveOkAsync(host, "conversation", Chain("""{"provider":"studio"}""", DialogueNode.Name));

        var run = await RunAsync(host, "conversation");

        run.Record.Status.ShouldBe(RunStatus.Failed);
        run.Record.Error.ShouldNotBeNull().Message.ShouldContain($"speech provider '{Provider}'");
        node.Attempts.ShouldBe(1, "a refused dialogue provider was retried as if it were a transient fault");
        engine.Calls.ShouldBe(0, "the tenant's turns must not reach a speech provider its policy forbids");
    }

    /// <summary>
    /// The refusal carries a stable error type, so the node retry and the
    /// recorded class read the type, never the text.
    /// </summary>
    /// <remarks>
    /// The refusal names the tenant and the allowed providers, both of them the
    /// operator's own words. Classified by its text, an allowed provider named
    /// "timeout-relay" made the refusal a timeout, a transient class, and the node ran
    /// three times against a provider its tenant forbids.
    /// </remarks>
    [Fact]
    public async Task A_refusal_whose_text_reads_like_a_transient_fault_is_classified_by_its_type_and_not_retried()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        var node = new DialogueNode();

        await using var host = await StartWorkflowAsync(engine, node);

        await SavePolicyAsync(host, "echo", "timeout-relay", "rate-limit-proxy");
        await SaveOkAsync(host, "conversation", Chain("""{"provider":"studio"}""", DialogueNode.Name));

        var run = await RunAsync(host, "conversation");

        run.Record.Status.ShouldBe(RunStatus.Failed);
        var error = run.Record.Error.ShouldNotBeNull();
        error.Type.ShouldBe("tenant_provider_refused");
        error.Class.ShouldBe(RunErrorClass.TenantProviderRefused);
        node.Attempts.ShouldBe(1, "a refusal whose text names 'timeout' was retried as a timeout");
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_policy_judges_the_dialogue_provider_name_not_the_registration_name()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await SavePolicyAsync(host, "studio");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"not allowed to call speech provider '{Provider}'");
        engine.Calls.ShouldBe(0, "a registration alias passed the tenant's egress policy");
    }

    [Fact]
    public async Task A_policy_that_names_the_provider_lets_the_dialogue_through()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await SavePolicyAsync(host, "echo", Provider);

        var dialogue = await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken);
        _ = await dialogue.SynthesizeDialogueAsync(Turns(), TestContext.Current.CancellationToken);

        dialogue.ShouldBeSameAs(engine, "a tenant without its own key gets the registered instance");
        engine.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_default_synthesizer_is_gated_too()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        await using var host = await StartAsync(engine, registerAsDefault: true);

        await SavePolicyAsync(host, "echo");

        await Should.ThrowAsync<TraconException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync(cancellationToken: TestContext.Current.CancellationToken));

        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_bound_tenant_is_refused_by_a_dialogue_provider_that_cannot_use_its_key()
    {
        var engine = new CountingDialogueSpeechProvider(Provider, supportsTenantCredentials: false);
        await using var host = await StartAsync(engine, keys: [(FlatKeyName, FlatKeyValue)]);
        await BindAsync(host, "default", FlatKeyName);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("own credential");
        exception.Message.ShouldContain("dialogue synthesizer");
        exception.Message.ShouldNotContain(FlatKeyValue);
        engine.Calls.ShouldBe(0, "a bound tenant was served on the host's key");
    }

    [Fact]
    public async Task A_bound_tenant_key_reaches_a_dialogue_provider_that_supports_it_and_replaces_the_callers()
    {
        var engine = new CountingDialogueSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine, keys: [(FlatKeyName, FlatKeyValue)]);
        await BindAsync(host, "default", FlatKeyName);

        var dialogue = await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken);

        // The capability is the same whether the tenant has its own key or not: the
        // wrapper implements the dialogue interface itself.
        dialogue.ProviderName.ShouldBe(Provider);
        dialogue.SupportsTenantCredentials.ShouldBeTrue();
        dialogue.DialogueCapabilities.ShouldBe(engine.DialogueCapabilities);

        _ = await dialogue.SynthesizeDialogueAsync(Turns(), TestContext.Current.CancellationToken);
        _ = await dialogue.SynthesizeDialogueAsync(
            Turns() with { Credential = new ModelProviderCredential { ApiKey = "somebody-else" } },
            TestContext.Current.CancellationToken);

        engine.Received.Select(static call => call.ApiKey).ShouldBe([FlatKeyValue, FlatKeyValue]);
    }

    [Fact]
    public async Task Two_tenants_in_parallel_each_send_their_own_key()
    {
        var engine = new CountingDialogueSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(
            engine,
            keys: [("Tracon:ProviderKeys:acme:Studio", "acme-studio-value"), ("Tracon:ProviderKeys:globex:Studio", "globex-studio-value")]);

        await BindAsync(host, "acme", "Tracon:ProviderKeys:acme:Studio");
        await BindAsync(host, "globex", "Tracon:ProviderKeys:globex:Studio");

        var calls = Enumerable.Range(0, 24).Select(index => Task.Run(async () =>
        {
            var tenant = index % 2 == 0 ? "acme" : "globex";

            // The run's tenant comes first, as for a queued workflow on a worker.
            TraconRunContext.SetCurrent(Scope(tenant));

            try
            {
                var dialogue = await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken);

                _ = await dialogue.SynthesizeDialogueAsync(
                    new SpeechDialogueRequest { Turns = [new SpeechDialogueTurn { Text = $"{tenant}:{index}", VoiceId = "a" }] },
                    TestContext.Current.CancellationToken);
            }
            finally
            {
                TraconRunContext.SetCurrent(null);
            }
        }));

        await Task.WhenAll(calls);

        engine.Calls.ShouldBe(24);

        foreach (var (text, apiKey) in engine.Received)
        {
            var tenant = text[..text.IndexOf(':', StringComparison.Ordinal)];

            apiKey.ShouldBe($"{tenant}-studio-value", $"the call '{text}' carried another tenant's key");
        }
    }

    [Fact]
    public async Task The_run_tenant_is_judged_before_the_request_tenant()
    {
        // The request's tenant (default) has no policy; the run's tenant forbids the
        // provider. A queued workflow runs where the request tenant is the worker's.
        var engine = new CountingDialogueSpeechProvider(Provider);
        await using var host = await StartAsync(engine);

        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("acme", ["echo"], TestContext.Current.CancellationToken);

        TraconRunContext.SetCurrent(Scope("acme"));

        try
        {
            var exception = await Should.ThrowAsync<TraconException>(
                async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken));

            exception.Message.ShouldContain("acme");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_binding_with_an_endpoint_override_is_refused()
    {
        var engine = new CountingDialogueSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine, keys: [(FlatKeyName, FlatKeyValue)]);
        await BindAsync(host, "default", FlatKeyName, endpoint: "https://proxy.example.com/");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("endpoint override");
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_binding_whose_key_has_no_value_is_refused_and_never_uses_the_setup_key()
    {
        var engine = new CountingDialogueSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine);
        await BindAsync(host, "default", FlatKeyName);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(FlatKeyName);
        engine.Calls.ShouldBe(0, "a binding with an empty key fell back to the host's key");
    }

    [Fact]
    public async Task A_synthesizer_that_cannot_voice_a_dialogue_is_named_and_never_called()
    {
        // Same for a tenant with a policy, one with its own key and one with neither:
        // the capability is read before any tenant decision.
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        var node = new DialogueNode();

        await using var host = await WorkflowSettingsFixtures.StartAsync(builder =>
        {
            builder.AddSpeechSynthesizer("studio", _ => engine);
            node.Register(builder);
        });
        await SaveOkAsync(host, "conversation", Chain("""{"provider":"studio"}""", DialogueNode.Name));

        var run = await RunAsync(host, "conversation");

        run.Record.Status.ShouldBe(RunStatus.Failed);
        run.Record.Error.ShouldNotBeNull().Message.ShouldContain($"synthesizer 'studio' (provider '{Provider}') cannot voice a dialogue");
        node.Attempts.ShouldBe(1);
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_failing_policy_store_fails_the_dialogue_before_the_provider()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        await using var host = await StartAsync(
            engine,
            configureServices: static services => services.AddSingleton<ITenantEgressPolicyStore>(new FailingEgressPolicyStore()));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", TestContext.Current.CancellationToken));

        engine.Calls.ShouldBe(0, "a failing policy store let the dialogue through");
    }

    [Fact]
    public async Task A_cancelled_resolution_never_hands_out_the_dialogue_provider()
    {
        var engine = new CountingDialogueSpeechProvider(Provider);
        var policies = new GatedEgressPolicyStore();
        await using var host = await StartAsync(
            engine,
            configureServices: services => services.AddSingleton<ITenantEgressPolicyStore>(policies));

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var resolution = Resolver(host).GetDialogueSynthesizerAsync("studio", cancellation.Token).AsTask();

        await policies.Entered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        policies.Release();

        await Should.ThrowAsync<OperationCanceledException>(resolution);
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Resolver(host).GetDialogueSynthesizerAsync("studio", cancellation.Token));
        policies.Reads.ShouldBe(1, "a token cancelled before the call must not even read the policy");
        engine.Calls.ShouldBe(0);
    }

    private static ISpeechProviderResolver Resolver(TraconTestHost host)
        => host.Services.GetRequiredService<ISpeechProviderResolver>();

    private static SpeechDialogueRequest Turns()
        => new()
        {
            Turns =
            [
                new SpeechDialogueTurn { Text = "hello", VoiceId = "a" },
                new SpeechDialogueTurn { Text = "hi", VoiceId = "b" },
            ],
        };

    private static AgentRunScope Scope(string tenant)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = tenant,
            SessionId = "session-1",
            AgentName = "podcast",
        };

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task BindAsync(TraconTestHost host, string tenantId, string keyName, string? endpoint = null)
        => host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = tenantId,
                ProviderName = Provider,
                ApiKeyConfigurationName = keyName,
                Endpoint = endpoint,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken).AsTask();

    private static Task<TraconTestHost> StartAsync(
        CountingDialogueSpeechProvider engine,
        bool registerAsDefault = false,
        Action<IServiceCollection>? configureServices = null,
        (string Key, string Value)[]? keys = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddSpeechSynthesizer("studio", _ => engine),
            configureServices: services =>
            {
                if (registerAsDefault)
                {
                    services.AddSingleton<ISpeechSynthesizer>(engine);
                }

                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection((keys ?? []).Select(static key => new KeyValuePair<string, string?>(key.Key, key.Value)))
                        .Build());

                configureServices?.Invoke(services);
            });

    private static Task<TraconTestHost> StartWorkflowAsync(CountingDialogueSpeechProvider engine, DialogueNode node)
        => WorkflowSettingsFixtures.StartAsync(builder =>
        {
            builder.AddSpeechSynthesizer("studio", _ => engine);
            node.Register(builder);
        });

    /// <summary>A function node that voices the last message's two halves as a two-turn dialogue.</summary>
    internal sealed class DialogueNode
    {
        public const string Name = "dialogue";

        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public void Register(ITraconBuilder builder)
            => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, DialogueSettings>(
                Name,
                provider =>
                {
                    // Taken at setup: the provider a node sees at call time is not the host's.
                    var speech = provider.GetRequiredService<ISpeechProviderResolver>();

                    return async (messages, settings, _, cancellationToken) =>
                    {
                        Interlocked.Increment(ref _attempts);

                        var dialogue = await speech.GetDialogueSynthesizerAsync(settings.Provider, cancellationToken);
                        var text = messages[^1].Text;
                        var result = await dialogue.SynthesizeDialogueAsync(
                            new SpeechDialogueRequest
                            {
                                Turns =
                                [
                                    new SpeechDialogueTurn { Text = text, VoiceId = "host" },
                                    new SpeechDialogueTurn { Text = text, VoiceId = "guest" },
                                ],
                                IncludeTimestamps = true,
                            },
                            cancellationToken);

                        return [new ChatMessage(ChatRole.User, $"{dialogue.ProviderName}:{result.Segments?.Count ?? 0} segments")];
                    };
                },
                static options => options.RetryPolicy = new WorkflowNodeRetryPolicy
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                });
    }

    /// <summary>The settings of the dialogue node.</summary>
    internal sealed record DialogueSettings
    {
        public string? Provider { get; init; }
    }

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

    /// <summary>An egress policy store whose read waits for the test, whatever the token says.</summary>
    private sealed class GatedEgressPolicyStore : ITenantEgressPolicyStore
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Reads => Volatile.Read(ref _reads);

        public void Release() => _gate.TrySetResult();

        public async ValueTask<TenantEgressPolicy?> GetAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            Entered.TrySetResult();

            await _gate.Task;

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
