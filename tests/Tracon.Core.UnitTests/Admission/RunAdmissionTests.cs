using System.Text.Json;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Admission;

/// <summary>
/// A host that runs an agent in process asks the same three questions the HTTP endpoints ask before it does: the run
/// authorization handler, session ownership and the tenant's quota. These cases hold the answers; the HTTP endpoints'
/// own responses are held by their gate tests, which render the same decisions.
/// </summary>
public sealed class RunAdmissionTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task A_host_with_no_rules_allows_every_run()
    {
        var harness = new Harness();

        var decision = await harness.CheckAsync();

        decision.IsAllowed.ShouldBeTrue();
        decision.Denial.ShouldBeNull();
        decision.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task A_denying_handler_refuses_with_its_own_reason_and_receives_the_start_request()
    {
        var handler = new ScriptedHandler(_ => RunAuthorizationResult.Deny("Not on the plan."));
        var harness = new Harness(handler: handler, userId: "u1");

        var decision = await harness.CheckAsync(sessionId: "s1");

        decision.IsAllowed.ShouldBeFalse();
        decision.Denial.ShouldBe(RunAdmissionDenial.NotAuthorized);
        decision.Reason.ShouldBe("Not on the plan.");

        var request = handler.Requests.ShouldHaveSingleItem();

        request.TenantId.ShouldBe(Tenant);
        request.AgentName.ShouldBe(Agent);
        request.SessionId.ShouldBe("s1");
        request.UserId.ShouldBe("u1");
        request.Access.ShouldBe(RunAccess.Start);
    }

    [Fact]
    public async Task A_handler_that_throws_denies_the_run_instead_of_letting_it_start()
    {
        var harness = new Harness(handler: new ScriptedHandler(_ => throw new InvalidOperationException("policy store is down")));

        var decision = await harness.CheckAsync();

        decision.IsAllowed.ShouldBeFalse();
        decision.Denial.ShouldBe(RunAdmissionDenial.NotAuthorized);
        decision.Reason.ShouldBe(RunAdmissionRules.FailedCheckDetail);
    }

    [Fact]
    public async Task A_handler_that_times_out_on_its_own_is_a_denial_and_the_callers_cancellation_is_not()
    {
        var timeout = new Harness(handler: new ScriptedHandler(_ => throw new TaskCanceledException("the policy call timed out")));

        (await timeout.CheckAsync()).Denial.ShouldBe(RunAdmissionDenial.NotAuthorized);

        using var source = new CancellationTokenSource();
        var handler = new ScriptedHandler(_ =>
        {
            source.Cancel();
            throw new OperationCanceledException(source.Token);
        });

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await new Harness(handler: handler).CheckAsync(cancellationToken: source.Token));
    }

    [Fact]
    public async Task With_ownership_off_a_session_owned_by_another_user_is_allowed()
    {
        var harness = new Harness(userId: "u2");

        await harness.SaveSessionAsync("s1", ownerId: "u1");

        (await harness.CheckAsync(sessionId: "s1")).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task With_ownership_on_the_owner_may_continue_and_another_user_may_not()
    {
        var owner = new Harness(ownership: new TraconSessionOwnershipOptions { Enabled = true }, userId: "u1");

        await owner.SaveSessionAsync("s1", ownerId: "u1");

        (await owner.CheckAsync(sessionId: "s1")).IsAllowed.ShouldBeTrue();

        var other = owner.WithUser("u2");
        var decision = await other.CheckAsync(sessionId: "s1");

        decision.IsAllowed.ShouldBeFalse();
        decision.Denial.ShouldBe(RunAdmissionDenial.SessionNotAuthorized);
        decision.Reason.ShouldBe(RunAdmissionRules.SessionNotAuthorizedDetail);
    }

    [Fact]
    public async Task An_unowned_session_and_a_session_of_another_user_get_the_same_refusal_when_unowned_ones_are_refused()
    {
        var harness = new Harness(
            ownership: new TraconSessionOwnershipOptions { Enabled = true, RefuseUnownedSessions = true },
            userId: "u1");

        await harness.SaveSessionAsync("unowned", ownerId: null);
        await harness.SaveSessionAsync("theirs", ownerId: "u2");

        var unowned = await harness.CheckAsync(sessionId: "unowned");
        var theirs = await harness.CheckAsync(sessionId: "theirs");

        unowned.Denial.ShouldBe(RunAdmissionDenial.SessionNotAuthorized);
        theirs.Denial.ShouldBe(RunAdmissionDenial.SessionNotAuthorized);
        unowned.Reason.ShouldBe(theirs.Reason);
    }

    [Fact]
    public async Task An_unowned_session_is_allowed_by_default()
    {
        var harness = new Harness(ownership: new TraconSessionOwnershipOptions { Enabled = true }, userId: "u1");

        await harness.SaveSessionAsync("old", ownerId: null);

        (await harness.CheckAsync(sessionId: "old")).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_session_with_no_identity_is_refused_when_an_owner_is_required_and_allowed_when_it_is_not()
    {
        var required = new Harness(ownership: new TraconSessionOwnershipOptions { Enabled = true }, userId: null);
        var refused = await required.CheckAsync(sessionId: "fresh");

        refused.Denial.ShouldBe(RunAdmissionDenial.SessionOwnerRequired);
        refused.Reason.ShouldBe(RunAdmissionRules.SessionOwnerRequiredDetail("fresh"));

        var optional = new Harness(
            ownership: new TraconSessionOwnershipOptions { Enabled = true, RequireAuthenticatedOwner = false },
            userId: null);

        (await optional.CheckAsync(sessionId: "fresh")).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_that_names_no_session_never_meets_the_ownership_rule()
    {
        var harness = new Harness(ownership: new TraconSessionOwnershipOptions { Enabled = true }, userId: null);

        (await harness.CheckAsync(sessionId: null)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task The_ambient_attribution_is_the_user_when_one_is_open_so_the_checked_user_is_the_one_who_owns_the_session()
    {
        var harness = new Harness(ownership: new TraconSessionOwnershipOptions { Enabled = true }, userId: "registered-user");

        await harness.SaveSessionAsync("s1", ownerId: "ambient-user");

        (await harness.CheckAsync(sessionId: "s1")).Denial.ShouldBe(RunAdmissionDenial.SessionNotAuthorized);

        using (AmbientRunAttributionScope.Begin("ambient-user", null))
        {
            (await harness.CheckAsync(sessionId: "s1")).IsAllowed.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task An_identity_source_that_throws_is_a_caller_with_no_identity()
    {
        var harness = new Harness(
            ownership: new TraconSessionOwnershipOptions { Enabled = true },
            attribution: new ThrowingAttribution());

        (await harness.CheckAsync(sessionId: "fresh")).Denial.ShouldBe(RunAdmissionDenial.SessionOwnerRequired);
    }

    [Fact]
    public async Task A_spent_run_quota_refuses_the_next_run_and_says_which_limit_and_when_it_resets()
    {
        var harness = new Harness();

        await harness.DefineQuotaAsync(maxRuns: 1);
        await harness.RecordRunAsync();

        var decision = await harness.CheckAsync();

        decision.IsAllowed.ShouldBeFalse();
        decision.Denial.ShouldBe(RunAdmissionDenial.QuotaExceeded);
        decision.QuotaMetric.ShouldBe(QuotaMetric.Runs);
        decision.QuotaPeriod.ShouldBe(QuotaPeriod.Daily);
        decision.QuotaLimit.ShouldBe(1);
        decision.QuotaUsed.ShouldBe(1);
        decision.QuotaResetsAt.ShouldNotBeNull();
        decision.QuotaAgentName.ShouldBe(Agent);
        decision.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_quota_the_run_has_not_reached_allows_it_and_a_quota_of_another_tenant_is_not_counted()
    {
        var harness = new Harness();

        await harness.DefineQuotaAsync(maxRuns: 2);
        await harness.RecordRunAsync();

        (await harness.CheckAsync()).IsAllowed.ShouldBeTrue();

        var other = new Harness(tenant: "globex", quotaStore: harness.QuotaStore, quotas: harness.Quotas);

        await other.RecordRunAsync();
        await other.RecordRunAsync();

        (await harness.CheckAsync()).IsAllowed.ShouldBeTrue("the other tenant's runs are in its own counter");
    }

    [Fact]
    public async Task A_quota_store_that_fails_follows_the_installation_setting_exactly_as_the_http_endpoints_do()
    {
        var closed = new Harness(quotaOptions: new TraconQuotaOptions { Enabled = true, AllowOnStoreFailure = false }, failingQuotaStore: true);

        (await closed.CheckAsync()).Denial.ShouldBe(RunAdmissionDenial.QuotaExceeded);

        var open = new Harness(quotaOptions: new TraconQuotaOptions { Enabled = true, AllowOnStoreFailure = true }, failingQuotaStore: true);

        (await open.CheckAsync()).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task The_handler_is_asked_before_ownership_and_ownership_before_quota()
    {
        var harness = new Harness(
            handler: new ScriptedHandler(_ => RunAuthorizationResult.Deny("no")),
            ownership: new TraconSessionOwnershipOptions { Enabled = true },
            userId: "u2");

        await harness.SaveSessionAsync("s1", ownerId: "u1");
        await harness.DefineQuotaAsync(maxRuns: 1);
        await harness.RecordRunAsync();

        (await harness.CheckAsync(sessionId: "s1")).Denial.ShouldBe(RunAdmissionDenial.NotAuthorized);

        var ownership = new Harness(
            ownership: new TraconSessionOwnershipOptions { Enabled = true },
            userId: "u2",
            quotaStore: harness.QuotaStore,
            quotas: harness.Quotas,
            sessions: harness.Sessions);

        (await ownership.CheckAsync(sessionId: "s1")).Denial.ShouldBe(RunAdmissionDenial.SessionNotAuthorized);
    }

    [Fact]
    public async Task A_blank_agent_name_is_refused()
    {
        await Should.ThrowAsync<ArgumentException>(async () => await new Harness().CheckAsync(agentName: " "));
    }

    private sealed class Harness
    {
        private readonly string _tenant;
        private readonly ManualClock _clock = new(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));
        private readonly IRunAttributionContext? _attribution;
        private readonly RunAdmission _admission;
        private readonly ScriptedHandler _handler;
        private readonly TraconSessionOwnershipOptions? _ownership;

        public Harness(
            string tenant = Tenant,
            ScriptedHandler? handler = null,
            TraconSessionOwnershipOptions? ownership = null,
            string? userId = null,
            IRunAttributionContext? attribution = null,
            TraconQuotaOptions? quotaOptions = null,
            IQuotaStore? quotaStore = null,
            QuotaEnforcer? quotas = null,
            ISessionStore? sessions = null,
            bool failingQuotaStore = false)
        {
            _tenant = tenant;
            _handler = handler ?? new ScriptedHandler(_ => RunAuthorizationResult.Allow());
            _ownership = ownership;
            _attribution = attribution ?? new FixedAttribution(userId);

            var tenants = new FixedTenantContext(tenant);

            Sessions = sessions ?? new InMemorySessionStore(tenants);
            QuotaStore = failingQuotaStore ? new FailingQuotaStore() : quotaStore ?? new InMemoryQuotaStore();
            Quotas = quotas ?? new QuotaEnforcer(
                QuotaStore,
                new StaticOptionsMonitor<TraconQuotaOptions>(quotaOptions ?? new TraconQuotaOptions { Enabled = true }),
                null,
                _clock);

            _admission = new RunAdmission(
                _handler,
                tenants,
                _attribution,
                ownership is null ? null : new StaticOptionsMonitor<TraconSessionOwnershipOptions>(ownership),
                Sessions,
                Quotas,
                null);
        }

        public ISessionStore Sessions { get; }

        public IQuotaStore QuotaStore { get; }

        public QuotaEnforcer Quotas { get; }

        public ValueTask<RunAdmissionDecision> CheckAsync(
            string agentName = Agent,
            string? sessionId = null,
            CancellationToken cancellationToken = default)
            => _admission.CheckAsync(agentName, sessionId, cancellationToken);

        public Harness WithUser(string userId)
            => new(_tenant, _handler, _ownership, userId, quotaStore: QuotaStore, quotas: Quotas, sessions: Sessions);

        public async Task SaveSessionAsync(string id, string? ownerId)
            => await Sessions.SaveAsync(
                new SessionRecord
                {
                    Id = id,
                    AgentName = Agent,
                    State = JsonDocument.Parse("{}").RootElement.Clone(),
                    CreatedAt = _clock.GetUtcNow(),
                    UpdatedAt = _clock.GetUtcNow(),
                    OwnerId = ownerId,
                },
                TestContext.Current.CancellationToken);

        public async Task DefineQuotaAsync(long maxRuns)
            => await QuotaStore.SaveAsync(
                new QuotaDefinition
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenant,
                    AgentName = Agent,
                    Period = QuotaPeriod.Daily,
                    MaxRuns = maxRuns,
                    Enabled = true,
                    CreatedAt = _clock.GetUtcNow(),
                    UpdatedAt = _clock.GetUtcNow(),
                },
                TestContext.Current.CancellationToken);

        public async Task RecordRunAsync()
            => await Quotas.RecordAsync(
                new QuotaConsumption
                {
                    TenantId = _tenant,
                    AgentName = Agent,
                    Runs = 1,
                    OccurredAt = _clock.GetUtcNow(),
                });
    }

    private sealed class ScriptedHandler(Func<RunAuthorizationRequest, RunAuthorizationResult> decide) : IRunAuthorizationHandler
    {
        public List<RunAuthorizationRequest> Requests { get; } = [];

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return new(decide(request));
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());
    }

    private sealed class FixedAttribution(string? userId) : IRunAttributionContext
    {
        public string? UserId => userId;

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class ThrowingAttribution : IRunAttributionContext
    {
        public string? UserId => throw new InvalidOperationException("the identity pipeline is down");

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class FailingQuotaStore : IQuotaStore
    {
        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");

        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("quota store is down");
    }
}
