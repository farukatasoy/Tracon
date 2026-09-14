using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 170 (F-231) at the host boundary. Which switches a deployment ended up
/// with is only decided once every module has registered, so the cases below
/// start real hosts rather than constructing the validator by hand.
/// </summary>
public sealed class ProductionProfileStartupTests
{
    [Fact]
    public async Task A_host_that_does_not_declare_the_profile_starts_unchanged()
    {
        await using var host = await TraconTestHost.StartAsync();

        var response = await host.Client.GetAsync(
            "/tracon/api/meta",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        // Not one line about the profile: an installation that never calls the
        // method pays nothing, not even a log entry.
        host.Logs.AllText.ShouldNotContain("Production profile");
    }

    [Theory]
    [InlineData(TraconProductionRisk.SingleTenant)]
    [InlineData(TraconProductionRisk.UnownedSessions)]
    [InlineData(TraconProductionRisk.UnencryptedContentAtRest)]
    [InlineData(TraconProductionRisk.UninspectedContent)]
    [InlineData(TraconProductionRisk.UnlimitedRequestRate)]
    [InlineData(TraconProductionRisk.UnboundedRetention)]
    public async Task One_permissive_decision_stops_the_host_and_is_named(TraconProductionRisk left)
    {
        // Every decision but one is answered, so the failure can only be about
        // the one left behind. A single test that leaves all six open would
        // pass even if five of the checks were broken.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: builder =>
                {
                    AnswerAllExcept(builder, left);
                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain(left.ToString());

        foreach (var answered in Enum.GetValues<TraconProductionRisk>().Where(risk => risk != left))
        {
            exception.Message.ShouldNotContain($"  {answered}");
        }
    }

    [Fact]
    public async Task A_host_that_answers_every_decision_starts()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, null);
                builder.RequireProductionProfile();
            });

        var response = await host.Client.GetAsync(
            "/tracon/api/meta",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_accepted_risk_starts_the_host_and_is_logged_by_name()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
                builder.RequireProductionProfile(static profile =>
                    profile.Accept(TraconProductionRisk.SingleTenant));
            });

        // One ENTRY has to carry both halves. Two separate ShouldContain calls
        // over the whole log pass when unrelated lines supply them separately —
        // "SingleTenantContext" already contains "SingleTenant".
        host.Logs.Entries.ShouldContain(entry =>
            entry.Contains($"{nameof(TraconProductionRisk.SingleTenant)} is accepted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_accept_does_not_cover_the_decision_next_to_it()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
                    Leave(builder, TraconProductionRisk.UnownedSessions);
                    builder.RequireProductionProfile(static profile =>
                        profile.Accept(TraconProductionRisk.SingleTenant));
                }));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.UnownedSessions));
        exception.Message.ShouldNotContain($"  {nameof(TraconProductionRisk.SingleTenant)}");
    }

    [Fact]
    public async Task Content_inspection_is_measured_by_the_registration_not_by_a_flag()
    {
        // AddTracon registers no guard and never adds the inspecting wrapper to
        // the model pipeline, so there is no flag to read: an empty
        // IEnumerable<IContentGuard> is what "off" looks like.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExcept(builder, TraconProductionRisk.UninspectedContent);
                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain(nameof(IContentGuard));
        exception.Message.ShouldContain("no content guard is registered");

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, null);
                builder.RequireProductionProfile();
            });

        host.Services.GetServices<IContentGuard>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_registered_guard_that_inspects_nothing_is_still_uninspected_content()
    {
        // The registration is necessary and not sufficient. Both inspection
        // switches default to on, but a deployment can turn both off and keep
        // the guard registered: the wrapper is then installed and inspects
        // nothing on every call, which is exactly the risk this item names.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExcept(builder, null);
                    builder.Services.Configure<TraconContentGuardOptions>(static options =>
                    {
                        options.InspectInput = false;
                        options.InspectOutput = false;
                    });
                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.UninspectedContent));
        exception.Message.ShouldContain("neither input nor output is inspected");
    }

    [Fact]
    public async Task A_guard_that_inspects_one_direction_answers_the_inspection_decision()
    {
        // Turning one direction off is a setting the deployment had to write, so
        // it is a decision already taken. The gate asks whether content is
        // inspected at all, not whether it is inspected in both directions.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, null);
                builder.Services.Configure<TraconContentGuardOptions>(static options =>
                    options.InspectOutput = false);
                builder.RequireProductionProfile();
            });

        var response = await host.Client.GetAsync(
            "/tracon/api/meta",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UseTenancy_with_resolution_off_is_still_reported_as_single_tenant()
    {
        // The case the core cannot see on its own: UseTenancy REPLACES the
        // tenant context, so "which ITenantContext is bound" answers satisfied,
        // while every request still falls to the default tenant. The HTTP
        // package's own check carries the same risk and the strictest wins.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
                    builder.UseTenancy(static options => options.Enabled = false);
                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.SingleTenant));
        exception.Message.ShouldContain("UseTenancy(options => options.Enabled)");
    }

    [Fact]
    public async Task UseTenancy_with_resolution_on_answers_the_tenant_decision()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.ClaimType = "tenant_id";
                });
                builder.RequireProductionProfile();
            });

        host.Services.GetRequiredService<IOptions<TraconTenancyOptions>>().Value.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task An_applications_own_tenant_context_answers_the_tenant_decision()
    {
        // No HTTP tenancy involved: a host that resolves the tenant from a
        // message header or a job is multi-tenant too, and the gate must not
        // insist on one particular route to the answer.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ITenantContext, StubTenantContext>(),
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
                builder.RequireProductionProfile();
            });

        host.Services.GetRequiredService<ITenantContext>().ShouldBeOfType<StubTenantContext>();
    }

    [Fact]
    public async Task The_gate_runs_in_a_host_with_no_HTTP_surface()
    {
        // No WebApplication and no MapTracon. These are composition decisions,
        // so an embedded host gets the same answer - including the tenant one,
        // which the core answers from the binding rather than an HTTP setting.
        using var host = BuildHostWithoutHttp(static builder =>
        {
            AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
            builder.RequireProductionProfile();
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.SingleTenant));
        exception.Message.ShouldContain(nameof(SingleTenantContext));
    }

    [Fact]
    public async Task Two_declarations_accept_the_union_rather_than_colliding()
    {
        // Two composition modules each declare the profile, which cannot be
        // registered with TryAdd. The second declaration must not be a conflict
        // and must not erase the first one's accept.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, TraconProductionRisk.SingleTenant);
                Leave(builder, TraconProductionRisk.UnboundedRetention);

                builder.RequireProductionProfile(static profile =>
                    profile.Accept(TraconProductionRisk.SingleTenant));
                builder.RequireProductionProfile(static profile =>
                    profile.Accept(TraconProductionRisk.UnboundedRetention));
            });

        host.Logs.Entries.ShouldContain(entry =>
            entry.Contains($"{nameof(TraconProductionRisk.SingleTenant)} is accepted", StringComparison.Ordinal));
        host.Logs.Entries.ShouldContain(entry =>
            entry.Contains($"{nameof(TraconProductionRisk.UnboundedRetention)} is accepted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_applications_own_check_joins_the_decision_and_the_strictest_answer_wins()
    {
        // A consumer's own check for a risk Tracon already covers: the two are
        // added side by side, so a deployment that satisfies Tracon's condition
        // but fails the consumer's own is still stopped.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureServices: static services =>
                    services.AddSingleton<IProductionProfileCheck, StricterRetentionCheck>(),
                configureTracon: static builder =>
                {
                    AnswerAllExcept(builder, null);
                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain("the archive job is not scheduled");
    }

    [Fact]
    public async Task Neither_the_failure_nor_the_log_carries_a_configured_value()
    {
        const string CanaryKeyId = "canary-4f2a-key-id";
        const string CanaryConfigurationKey = "CanaryKeys:canary-4f2a";

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExcept(builder, TraconProductionRisk.UnencryptedContentAtRest);

                    // Protection is OFF, but its key map is already filled in -
                    // the shape an upgrading deployment is in the moment before
                    // it turns the flag on. The gate reports the flag; the key
                    // ids and the configuration key names stay out of the
                    // message and out of the log.
                    builder.Services.Configure<TraconContentProtectionOptions>(static options =>
                        options.Keys[CanaryKeyId] = CanaryConfigurationKey);

                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain("Tracon:ContentProtection:Enabled");
        exception.Message.ShouldNotContain(CanaryKeyId);
        exception.Message.ShouldNotContain(CanaryConfigurationKey);
    }

    private static void AnswerAllExcept(ITraconBuilder builder, TraconProductionRisk? left)
    {
        foreach (var risk in Enum.GetValues<TraconProductionRisk>())
        {
            if (risk == left)
            {
                continue;
            }

            Answer(builder, risk);
        }
    }

    private static void Answer(ITraconBuilder builder, TraconProductionRisk risk)
    {
        switch (risk)
        {
            case TraconProductionRisk.SingleTenant:
                builder.Services.AddSingleton<ITenantContext, StubTenantContext>();
                break;

            case TraconProductionRisk.UnownedSessions:
                builder.Services.Configure<TraconSessionOwnershipOptions>(static options => options.Enabled = true);
                break;

            case TraconProductionRisk.UnencryptedContentAtRest:
                builder.Services.Configure<TraconContentProtectionOptions>(static options =>
                {
                    options.Enabled = true;
                    options.ActiveKeyId = "test";
                    options.Keys["test"] = "TestContentProtectionKeys:test";
                });
                break;

            case TraconProductionRisk.UninspectedContent:
                builder.AddPatternContentGuard();
                break;

            case TraconProductionRisk.UnlimitedRequestRate:
                builder.Services.Configure<TraconRateLimitOptions>(static options => options.Enabled = true);
                break;

            case TraconProductionRisk.UnboundedRetention:
                builder.Services.Configure<TraconRetentionOptions>(static options => options.Enabled = true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(risk), risk, "Unknown production risk.");
        }
    }

    /// <summary>Puts one decision back on its permissive default.</summary>
    private static void Leave(ITraconBuilder builder, TraconProductionRisk risk)
    {
        switch (risk)
        {
            case TraconProductionRisk.UnownedSessions:
                builder.Services.Configure<TraconSessionOwnershipOptions>(static options => options.Enabled = false);
                break;

            case TraconProductionRisk.UnboundedRetention:
                builder.Services.Configure<TraconRetentionOptions>(static options => options.Enabled = false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(risk), risk, "Only flag-shaped decisions can be undone.");
        }
    }

    private static IHost BuildHostWithoutHttp(Action<ITraconBuilder> configureTracon)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        var tracon = builder.Services.AddTracon();
        tracon.AddModelProvider(new Tracon.Testing.FakeModelProvider("echo").EchoesUserMessage());
        configureTracon(tracon);

        return builder.Build();
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public string TenantId => "stub-tenant";
    }

    private sealed class StricterRetentionCheck : IProductionProfileCheck
    {
        public TraconProductionRisk Risk => TraconProductionRisk.UnboundedRetention;

        public ProductionProfileResult Evaluate(IServiceProvider services)
            => ProductionProfileResult.Permissive(
                "Contoso:Archive:Enabled",
                "the archive job is not scheduled",
                "schedule the archive job");
    }
}
