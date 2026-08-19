using System.Globalization;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Configuration;

namespace AgentPrism.Core.UnitTests.Triggers;

/// <summary>Verifies <see cref="InboundTriggerDispatcher"/> (phase 66).</summary>
public sealed class InboundTriggerDispatcherTests
{
    private const string TenantId = "default";
    private const string TriggerName = "slack";
    private const string SecretConfigurationKey = "AgentPrism:TriggerSecrets:Slack";
    private const string Secret = "whsec_test";
    private const string Body = """{"event":{"text":"hello from slack"}}""";

    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unknown_trigger_name_is_unauthorized()
    {
        var (dispatcher, _) = CreateDispatcher();

        var result = await dispatcher.ValidateAsync(TenantId, "missing", Body, null, null);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task Unknown_tenant_is_unauthorized_and_does_not_fall_back_to_a_default_tenant()
    {
        // K-382's rule, applied here: a trigger registered for "default" must
        // NEVER answer a request addressed to an unrelated tenant segment.
        var (dispatcher, _) = await CreateDispatcherWithSavedTriggerAsync();

        var result = await dispatcher.ValidateAsync("some-other-tenant", TriggerName, Body, null, null);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task Disabled_trigger_is_unauthorized()
    {
        var store = new InMemoryInboundTriggerStore();
        await store.UpsertAsync(NewTrigger(enabled: false));

        var (dispatcher, clock) = CreateDispatcher(triggerStore: store);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task Unknown_trigger_name_and_a_wrong_signature_on_a_real_trigger_produce_the_identical_result()
    {
        // 66.2: the caller must not be able to enumerate trigger names by
        // comparing responses — this asserts the outcomes are the SAME
        // discriminated value, not just "both happen to map to 401".
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        var missingTrigger = await dispatcher.ValidateAsync(TenantId, "no-such-trigger", Body, timestamp, "sha256=deadbeef");
        var wrongSignature = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, "sha256=deadbeef");

        missingTrigger.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
        wrongSignature.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
        missingTrigger.ErrorDetail.ShouldBe(wrongSignature.ErrorDetail);
    }

    [Fact]
    public async Task Missing_signature_is_unauthorized()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, null);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task Missing_timestamp_is_unauthorized()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, null, signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task Wrong_signature_is_unauthorized()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, "sha256=deadbeef");

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task A_tampered_body_with_the_original_signature_is_unauthorized()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var result = await dispatcher.ValidateAsync(
            TenantId, TriggerName, """{"event":{"text":"tampered"}}""", timestamp, signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task A_stale_timestamp_outside_the_tolerance_window_is_unauthorized()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var staleTime = clock.GetUtcNow().AddMinutes(-10);
        var signature = WebhookSigner.Sign(Body, staleTime, Secret);

        var result = await dispatcher.ValidateAsync(
            TenantId, TriggerName, Body, staleTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
    }

    [Fact]
    public async Task A_correctly_signed_request_within_the_window_is_valid()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.Valid);
        result.Validated.ShouldNotBeNull();
        result.Validated!.Message.ShouldBe(Body);
    }

    [Fact]
    public async Task The_same_signature_replayed_a_second_time_is_rejected()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var first = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);
        var second = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);

        first.Outcome.ShouldBe(InboundTriggerOutcome.Valid);
        second.Outcome.ShouldBe(InboundTriggerOutcome.Replayed);
    }

    [Fact]
    public async Task ReleaseAsync_lets_the_same_signature_be_reserved_again()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var first = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);
        await dispatcher.ReleaseAsync(first.Validated!);

        var second = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);

        second.Outcome.ShouldBe(InboundTriggerOutcome.Valid);
    }

    [Fact]
    public async Task Malformed_json_body_is_an_invalid_payload()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync();
        const string malformedBody = "{not json";
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(malformedBody, clock.GetUtcNow(), Secret);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, malformedBody, timestamp, signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.InvalidPayload);
    }

    [Fact]
    public async Task An_unresolved_payload_path_is_an_invalid_payload()
    {
        var store = new InMemoryInboundTriggerStore();
        await store.UpsertAsync(NewTrigger() with
        {
            PayloadMode = InboundTriggerPayloadMode.Path,
            PayloadPath = "event.missing",
        });

        var (dispatcher, clock) = CreateDispatcher(triggerStore: store);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var result = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);

        result.Outcome.ShouldBe(InboundTriggerOutcome.InvalidPayload);
    }

    [Fact]
    public async Task The_trigger_rate_limit_rejects_requests_beyond_the_per_minute_cap()
    {
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync(
            options: new AgentPrismInboundTriggerOptions { MaxRequestsPerMinute = 1 });
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // Two DIFFERENT, VALIDLY signed bodies: the rate limit is checked
        // AFTER signature verification (a request that never proved it holds
        // the secret must not be able to burn a legitimate sender's budget —
        // see InboundTriggerDispatcher.ValidateAsync's ordering remark), so
        // both requests must pass signature verification to reach the limiter.
        const string firstBody = """{"event":{"text":"first"}}""";
        const string secondBody = """{"event":{"text":"second"}}""";
        var firstSignature = WebhookSigner.Sign(firstBody, clock.GetUtcNow(), Secret);
        var secondSignature = WebhookSigner.Sign(secondBody, clock.GetUtcNow(), Secret);

        var first = await dispatcher.ValidateAsync(TenantId, TriggerName, firstBody, timestamp, firstSignature);
        var second = await dispatcher.ValidateAsync(TenantId, TriggerName, secondBody, timestamp, secondSignature);

        first.Outcome.ShouldBe(InboundTriggerOutcome.Valid);
        second.Outcome.ShouldBe(InboundTriggerOutcome.RateLimited);
    }

    [Fact]
    public async Task An_unsigned_flood_does_not_consume_a_legitimate_senders_rate_limit_budget()
    {
        // The 🔴 case the reordering closes: garbage requests must not be
        // able to exhaust the budget a real, signed request needs.
        var (dispatcher, clock) = await CreateDispatcherWithSavedTriggerAsync(
            options: new AgentPrismInboundTriggerOptions { MaxRequestsPerMinute = 1 });
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        for (var i = 0; i < 10; i++)
        {
            var forged = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, "sha256=deadbeef");
            forged.Outcome.ShouldBe(InboundTriggerOutcome.Unauthorized);
        }

        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);
        var legitimate = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);

        legitimate.Outcome.ShouldBe(InboundTriggerOutcome.Valid);
    }

    [Fact]
    public async Task EnqueueAsync_for_an_agent_target_opens_a_queued_run_with_the_same_id_as_the_job()
    {
        var (dispatcher, clock, jobStore, runStore, _) = await CreateFullDispatcherWithSavedTriggerAsync();
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var validation = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);
        var dispatched = await dispatcher.EnqueueAsync(validation.Validated!);

        dispatched.RunId.ShouldNotBeNull();
        dispatched.JobId.ShouldBe(dispatched.RunId!.Value);

        var run = await runStore.GetRunAsync(dispatched.RunId!.Value);
        run.ShouldNotBeNull();
        run!.Status.ShouldBe(RunStatus.Queued);
        run.AgentName.ShouldBe("demo-agent");

        var job = await jobStore.GetAsync(TenantId, dispatched.JobId);
        job.ShouldNotBeNull();
        job!.Kind.ShouldBe(JobKind.AgentRun);
    }

    [Fact]
    public async Task EnqueueAsync_for_a_workflow_target_queues_a_single_item_job_with_no_run_id()
    {
        var store = new InMemoryInboundTriggerStore();
        await store.UpsertAsync(NewTrigger() with
        {
            TargetKind = InboundTriggerTargetKind.Workflow,
            TargetName = "demo-workflow",
        });

        var jobStore = new InMemoryJobStore();
        var runStore = new InMemoryRunStore();
        var (dispatcher, clock) = CreateDispatcher(triggerStore: store, jobStore: jobStore, runStore: runStore);

        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = WebhookSigner.Sign(Body, clock.GetUtcNow(), Secret);

        var validation = await dispatcher.ValidateAsync(TenantId, TriggerName, Body, timestamp, signature);
        var dispatched = await dispatcher.EnqueueAsync(validation.Validated!);

        dispatched.RunId.ShouldBeNull();

        var job = await jobStore.GetAsync(TenantId, dispatched.JobId);
        job.ShouldNotBeNull();
        job!.Kind.ShouldBe(JobKind.Workflow);
        job.TargetName.ShouldBe("demo-workflow");

        var items = await jobStore.ListItemsAsync(dispatched.JobId);
        items.ShouldHaveSingleItem();
        items[0].Input.ShouldBe(Body);
    }

    private static InboundTrigger NewTrigger(bool enabled = true)
        => new()
        {
            TenantId = TenantId,
            Name = TriggerName,
            TargetKind = InboundTriggerTargetKind.Agent,
            TargetName = "demo-agent",
            SigningSecretConfigurationName = SecretConfigurationKey,
            PayloadMode = InboundTriggerPayloadMode.WholeBody,
            Enabled = enabled,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static async Task<(InboundTriggerDispatcher Dispatcher, ManualTimeProvider Clock)> CreateDispatcherWithSavedTriggerAsync(
        AgentPrismInboundTriggerOptions? options = null)
    {
        var store = new InMemoryInboundTriggerStore();
        await store.UpsertAsync(NewTrigger());

        return CreateDispatcher(triggerStore: store, options: options);
    }

    private static async Task<(
        InboundTriggerDispatcher Dispatcher,
        ManualTimeProvider Clock,
        InMemoryJobStore JobStore,
        InMemoryRunStore RunStore,
        InMemoryInboundTriggerStore TriggerStore)> CreateFullDispatcherWithSavedTriggerAsync()
    {
        var store = new InMemoryInboundTriggerStore();
        await store.UpsertAsync(NewTrigger());

        var jobStore = new InMemoryJobStore();
        var runStore = new InMemoryRunStore();
        var (dispatcher, clock) = CreateDispatcher(triggerStore: store, jobStore: jobStore, runStore: runStore);

        return (dispatcher, clock, jobStore, runStore, store);
    }

    private static (InboundTriggerDispatcher Dispatcher, ManualTimeProvider Clock) CreateDispatcher(
        InMemoryInboundTriggerStore? triggerStore = null,
        InMemoryJobStore? jobStore = null,
        InMemoryRunStore? runStore = null,
        AgentPrismInboundTriggerOptions? options = null,
        ManualTimeProvider? clock = null)
    {
        clock ??= new ManualTimeProvider(Now);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(SecretConfigurationKey, Secret)])
            .Build();

        var dispatcher = new InboundTriggerDispatcher(
            triggerStore ?? new InMemoryInboundTriggerStore(),
            new InboundTriggerSecretResolver(
                configuration,
                new StaticOptionsMonitor<AgentPrismInboundTriggerOptions>(new AgentPrismInboundTriggerOptions())),
            new InboundTriggerRateLimiter(
                new StaticOptionsMonitor<AgentPrismInboundTriggerOptions>(options ?? new AgentPrismInboundTriggerOptions()),
                clock),
            new InMemoryIdempotencyStore(),
            jobStore ?? new InMemoryJobStore(clock),
            runStore ?? new InMemoryRunStore(),
            new StaticOptionsMonitor<AgentPrismInboundTriggerOptions>(options ?? new AgentPrismInboundTriggerOptions()),
            clock);

        return (dispatcher, clock);
    }
}
