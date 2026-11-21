using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host that runs an agent in process and asks <see cref="RunAdmission"/> gets the answer the HTTP run endpoint gives
/// for the same request: the same decision and the same words. The two surfaces render one set of rules, and these
/// cases hold that they keep doing so.
/// </summary>
/// <remarks>
/// They sit at the functional level because the claim crosses the boundary a unit test does not have: the identity the
/// request resolved, the session the first request wrote and the counter the first run advanced are all state of a real
/// host that the second request, and the admission call, read back.
/// </remarks>
public sealed class RunAdmissionHttpParityTests
{
    private const string Agent = "kod-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{Agent}/run", UriKind.Relative);

    [Fact]
    public async Task Another_users_session_is_refused_over_http_and_by_admission_in_the_same_words()
    {
        var identity = new SwitchableAttribution();

        await using var host = await StartAsync(identity, handler: null);

        identity.UserId = "a";
        (await PostAsync(host, "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);

        identity.UserId = "b";

        using var refused = await PostAsync(host, "s1");
        var problem = await TraconTestHost.ReadJsonAsync(refused);
        var decision = await Admission(host).CheckAsync(Agent, "s1", TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        decision.IsAllowed.ShouldBeFalse();
        decision.Denial.ShouldBe(RunAdmissionDenial.SessionNotAuthorized);
        decision.Reason.ShouldBe(problem.GetProperty("detail").GetString());

        identity.UserId = "a";

        (await Admission(host).CheckAsync(Agent, "s1", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_session_nobody_can_own_is_refused_over_http_and_by_admission_in_the_same_words()
    {
        var identity = new SwitchableAttribution();

        await using var host = await StartAsync(identity, handler: null);

        identity.UserId = null;

        using var refused = await PostAsync(host, "fresh");
        var problem = await TraconTestHost.ReadJsonAsync(refused);
        var decision = await Admission(host).CheckAsync(Agent, "fresh", TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        decision.Denial.ShouldBe(RunAdmissionDenial.SessionOwnerRequired);
        decision.Reason.ShouldBe(problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_denying_handler_is_refused_over_http_and_by_admission_with_the_handlers_reason()
    {
        var identity = new SwitchableAttribution { UserId = "a" };

        await using var host = await StartAsync(identity, new DenyingHandler("Not on the plan."));

        using var refused = await PostAsync(host, sessionId: null);
        var problem = await TraconTestHost.ReadJsonAsync(refused);
        var decision = await Admission(host).CheckAsync(Agent, null, TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        decision.Denial.ShouldBe(RunAdmissionDenial.NotAuthorized);
        decision.Reason.ShouldBe("Not on the plan.");
        decision.Reason.ShouldBe(problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_spent_quota_is_refused_over_http_and_by_admission_with_the_same_limit()
    {
        var identity = new SwitchableAttribution { UserId = "a" };

        await using var host = await StartAsync(identity, handler: null);

        await host.Services.GetRequiredService<IQuotaStore>().SaveAsync(
            new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.DefaultTenantId,
                AgentName = Agent,
                Period = QuotaPeriod.Daily,
                MaxRuns = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        (await PostAsync(host, sessionId: null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refused = await PostAsync(host, sessionId: null);
        var problem = await TraconTestHost.ReadJsonAsync(refused);
        var decision = await Admission(host).CheckAsync(Agent, null, TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        decision.Denial.ShouldBe(RunAdmissionDenial.QuotaExceeded);
        decision.Reason.ShouldBe(problem.GetProperty("detail").GetString());
        decision.QuotaLimit.ShouldBe(problem.GetProperty("quotaLimit").GetDecimal());
        decision.QuotaUsed.ShouldBe(problem.GetProperty("quotaUsed").GetDecimal());
        decision.QuotaMetric?.ToString().ShouldBe(problem.GetProperty("quotaMetric").GetString());
    }

    [Fact]
    public async Task A_host_with_no_rule_in_the_way_is_allowed_over_http_and_by_admission()
    {
        var identity = new SwitchableAttribution { UserId = "a" };

        await using var host = await StartAsync(identity, handler: null);

        (await PostAsync(host, "open")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Admission(host).CheckAsync(Agent, "open", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
    }

    private static RunAdmission Admission(TraconTestHost host) => host.Services.GetRequiredService<RunAdmission>();

    private static async Task<HttpResponseMessage> PostAsync(TraconTestHost host, string? sessionId)
        => await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "hello", SessionId = sessionId },
            TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(SwitchableAttribution identity, IRunAuthorizationHandler? handler)
        => TraconTestHost.StartAsync(
            builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(options => options.Enabled = true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));

                if (handler is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton(handler));
                }
            });

    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class DenyingHandler(string reason) : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Deny(reason));

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());
    }
}
