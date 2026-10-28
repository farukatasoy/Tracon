using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    [InlineData(TraconProductionRisk.UnregisteredRolePolicies)]
    [InlineData(TraconProductionRisk.UncheckedToolCalls)]
    [InlineData(TraconProductionRisk.UnrestrictedTenantEgress)]
    [InlineData(TraconProductionRisk.UnverifiedTenantHeader)]
    [InlineData(TraconProductionRisk.UnlimitedTenantWorkflows)]
    public async Task One_permissive_decision_stops_the_host_and_is_named(TraconProductionRisk left)
    {
        // Every decision but one is answered, so the failure can only be about
        // the one left behind. A single test that leaves every decision open would
        // pass even if all but one of the checks were broken.
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

    [Fact]
    public async Task The_phase_259_decisions_name_their_settings_and_carry_no_configured_value()
    {
        // Each new check reads a setting that holds a string the deployment chose: the header name and the
        // allowed key prefix. They sit in the options the gate reads, with every new decision left open, and
        // neither may reach the failure or the log. Role policy names are Tracon's own constants.
        const string CanaryHeader = "X-Canary-Header-7d3e1c";
        const string CanaryPrefix = "CanaryProviderKeys-7d3e1c:";

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExceptThese(
                        builder,
                        TraconProductionRisk.UnregisteredRolePolicies,
                        TraconProductionRisk.UncheckedToolCalls,
                        TraconProductionRisk.UnrestrictedTenantEgress,
                        TraconProductionRisk.UnverifiedTenantHeader,
                        TraconProductionRisk.UnlimitedTenantWorkflows);

                    builder.Services.Configure<TraconTenancyOptions>(static options => options.HeaderName = CanaryHeader);
                    builder.Services.Configure<TraconTenantProviderOptions>(static options =>
                        options.AllowedConfigurationPrefix = CanaryPrefix);

                    builder.RequireProductionProfile();
                }));

        foreach (var left in new[]
        {
            nameof(TraconProductionRisk.UnregisteredRolePolicies),
            nameof(TraconProductionRisk.UncheckedToolCalls),
            nameof(TraconProductionRisk.UnrestrictedTenantEgress),
            nameof(TraconProductionRisk.UnverifiedTenantHeader),
            nameof(TraconProductionRisk.UnlimitedTenantWorkflows),
        })
        {
            exception.Message.ShouldContain($"  {left}");
        }

        // Every open decision gives the three lines the reader needs.
        exception.Message.ShouldContain("AddAuthorization(TraconPolicies.*)");
        exception.Message.ShouldContain(nameof(IToolAuthorizationHandler));
        exception.Message.ShouldContain("Tracon:TenantProviders:RequireEgressPolicy");
        exception.Message.ShouldContain("UseTenancy(options => options.AllowHeaderResolution)");
        exception.Message.ShouldContain("Tracon:Workflows:MaxConcurrentRunsPerTenant");

        exception.Message.ShouldNotContain(CanaryHeader);
        exception.Message.ShouldNotContain(CanaryPrefix);
    }

    [Fact]
    public async Task A_multi_tenant_host_that_answers_nothing_has_every_decision_but_SingleTenant_open()
    {
        // SingleTenant cannot be open together with the tenant egress, tenant header and workflow slot
        // decisions: those are asked only when tenants ARE separated, and SingleTenant is open only when
        // they are not. So the widest message is every decision but that one. Reporting only the first open
        // decision would turn one failure into ten restarts.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                    builder.UseWorkflows();
                    builder.RequireProductionProfile();
                }));

        foreach (var risk in Enum.GetValues<TraconProductionRisk>().Where(static risk => risk != TraconProductionRisk.SingleTenant))
        {
            exception.Message.ShouldContain($"  {risk}");
        }

        exception.Message.ShouldNotContain($"  {nameof(TraconProductionRisk.SingleTenant)}");
        exception.Message.ShouldContain($"{Enum.GetValues<TraconProductionRisk>().Length - 1} production decisions");
    }

    [Fact]
    public async Task A_single_tenant_host_that_answers_nothing_is_not_asked_the_three_tenant_decisions()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    builder.UseWorkflows();
                    builder.RequireProductionProfile();
                }));

        foreach (var risk in new[]
        {
            TraconProductionRisk.UnrestrictedTenantEgress,
            TraconProductionRisk.UnverifiedTenantHeader,
            TraconProductionRisk.UnlimitedTenantWorkflows,
        })
        {
            exception.Message.ShouldNotContain($"  {risk}");
        }

        exception.Message.ShouldContain($"  {nameof(TraconProductionRisk.SingleTenant)}");
        exception.Message.ShouldContain($"  {nameof(TraconProductionRisk.UnregisteredRolePolicies)}");
        exception.Message.ShouldContain($"  {nameof(TraconProductionRisk.UncheckedToolCalls)}");
    }

    [Fact]
    public async Task Each_phase_259_decision_that_is_accepted_starts_the_host_and_is_logged_by_name()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExceptThese(
                    builder,
                    TraconProductionRisk.UnregisteredRolePolicies,
                    TraconProductionRisk.UncheckedToolCalls,
                    TraconProductionRisk.UnrestrictedTenantEgress,
                    TraconProductionRisk.UnverifiedTenantHeader,
                    TraconProductionRisk.UnlimitedTenantWorkflows);

                builder.RequireProductionProfile(static profile => profile
                    .Accept(TraconProductionRisk.UnregisteredRolePolicies)
                    .Accept(TraconProductionRisk.UncheckedToolCalls)
                    .Accept(TraconProductionRisk.UnrestrictedTenantEgress)
                    .Accept(TraconProductionRisk.UnverifiedTenantHeader)
                    .Accept(TraconProductionRisk.UnlimitedTenantWorkflows));
            });

        foreach (var accepted in new[]
        {
            TraconProductionRisk.UnregisteredRolePolicies,
            TraconProductionRisk.UncheckedToolCalls,
            TraconProductionRisk.UnrestrictedTenantEgress,
            TraconProductionRisk.UnverifiedTenantHeader,
            TraconProductionRisk.UnlimitedTenantWorkflows,
        })
        {
            // One entry carries the name and the word, not two unrelated lines.
            host.Logs.Entries.ShouldContain(
                entry => entry.Contains($"{accepted} is accepted", StringComparison.Ordinal),
                $"{accepted} is not logged as accepted.");
        }
    }

    [Fact]
    public async Task A_single_tenant_host_is_not_asked_about_tenant_egress_or_workflow_slots()
    {
        // The built-in single-tenant context is bound, UseWorkflows has no per-tenant cap and
        // RequireEgressPolicy is off: both decisions are about a tenant crowding out ANOTHER tenant, and
        // there is none. SingleTenant itself is accepted by name, which is the host's own decision.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExceptThese(
                    builder,
                    TraconProductionRisk.SingleTenant,
                    TraconProductionRisk.UnrestrictedTenantEgress,
                    TraconProductionRisk.UnlimitedTenantWorkflows);

                builder.RequireProductionProfile(static profile => profile.Accept(TraconProductionRisk.SingleTenant));
            });

        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnrestrictedTenantEgress)} is not applicable", StringComparison.Ordinal));
        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnlimitedTenantWorkflows)} is not applicable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UseTenancy_with_resolution_off_is_not_asked_about_tenant_egress_or_workflow_slots()
    {
        // UseTenancy REPLACES the tenant context, so asking which type is bound would call this host
        // multi-tenant. Resolution is off: every request is the default tenant, and the host has accepted
        // SingleTenant by name.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExceptThese(
                    builder,
                    TraconProductionRisk.SingleTenant,
                    TraconProductionRisk.UnrestrictedTenantEgress,
                    TraconProductionRisk.UnlimitedTenantWorkflows);

                builder.UseTenancy(static options => options.Enabled = false);
                builder.RequireProductionProfile(static profile => profile.Accept(TraconProductionRisk.SingleTenant));
            });

        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnrestrictedTenantEgress)} is not applicable", StringComparison.Ordinal));
        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnlimitedTenantWorkflows)} is not applicable", StringComparison.Ordinal));
        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnverifiedTenantHeader)} is not applicable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_workflow_execution_is_not_asked_about_workflow_slots()
    {
        // Execution off: no run takes a slot, so no tenant can take them all.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExceptThese(builder, TraconProductionRisk.UnlimitedTenantWorkflows);

                builder.UseWorkflows(static options => options.Enabled = false);
                builder.RequireProductionProfile();
            });

        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnlimitedTenantWorkflows)} is not applicable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_claim_type_or_a_closed_header_path_answers_the_tenant_header_decision(bool withClaimType)
    {
        // true: the header path is open but a claim type takes precedence, so the header is never read.
        // false: no claim type, and the header path is closed.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                AnswerAllExcept(builder, null);
                builder.UseTenancy(options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = withClaimType;
                    options.ClaimType = withClaimType ? "tenant_id" : null;
                });
                builder.RequireProductionProfile();
            });

        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnverifiedTenantHeader)} is satisfied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Registered_role_policies_answer_the_role_decision_and_name_the_missing_ones_when_not()
    {
        await using (var answered = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, null);
                builder.RequireProductionProfile();
            }))
        {
            answered.Logs.Entries.ShouldContain(
                entry => entry.Contains($"{nameof(TraconProductionRisk.UnregisteredRolePolicies)} is satisfied", StringComparison.Ordinal));
        }

        // Two of the three: the message counts what is missing and names exactly those.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExceptThese(builder, TraconProductionRisk.UnregisteredRolePolicies);
                    TestAuthenticationHandler.Add(builder.Services)
                        .AddAuthorizationBuilder()
                        .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true));
                    builder.RequireProductionProfile();
                }));

        exception.Message.ShouldContain("2 of the 3 role policies are not registered");
        exception.Message.ShouldContain(TraconPolicies.Operator);
        exception.Message.ShouldContain(TraconPolicies.Admin);
    }

    [Fact]
    public async Task A_host_that_never_maps_Tracon_is_not_asked_about_role_policies()
    {
        // No MapTracon, so nothing learned which policies exist: the decision has no check and is reported
        // as not applicable. The role policies are an HTTP surface question.
        var logs = new RecordingLoggerProvider();

        using var host = BuildHostWithoutHttp(
            static builder =>
            {
                AnswerAllExcept(builder, null);
                builder.RequireProductionProfile();
            },
            logs);

        await host.StartAsync(TestContext.Current.CancellationToken);

        logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UnregisteredRolePolicies)} is not applicable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_applications_tool_authorization_handler_answers_the_tool_decision()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                AnswerAllExcept(builder, null);
                builder.RequireProductionProfile();
            });

        host.Services.GetRequiredService<IToolAuthorizationHandler>().ShouldBeOfType<StubToolAuthorizationHandler>();
        host.Logs.Entries.ShouldContain(
            entry => entry.Contains($"{nameof(TraconProductionRisk.UncheckedToolCalls)} is satisfied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_tool_decision_is_asked_of_a_single_tenant_host_too()
    {
        // Tool authorization does not depend on tenants.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    AnswerAllExceptThese(builder, TraconProductionRisk.UncheckedToolCalls, TraconProductionRisk.SingleTenant);
                    builder.RequireProductionProfile(static profile => profile.Accept(TraconProductionRisk.SingleTenant));
                }));

        exception.Message.ShouldContain($"  {nameof(TraconProductionRisk.UncheckedToolCalls)}");
        exception.Message.ShouldContain(nameof(AllowAllToolAuthorizationHandler));
    }

    private static void AnswerAllExcept(ITraconBuilder builder, TraconProductionRisk? left)
        => AnswerAllExceptThese(builder, left is { } one ? [one] : []);

    private static void AnswerAllExceptThese(ITraconBuilder builder, params TraconProductionRisk[] left)
    {
        foreach (var risk in Enum.GetValues<TraconProductionRisk>())
        {
            if (left.Contains(risk))
            {
                continue;
            }

            Answer(builder, risk);
        }

        foreach (var risk in left)
        {
            Open(builder, risk);
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

            case TraconProductionRisk.UnregisteredRolePolicies:
                TestAuthenticationHandler.Add(builder.Services)
                    .AddAuthorizationBuilder()
                    .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                    .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                    .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true));
                break;

            case TraconProductionRisk.UncheckedToolCalls:
                builder.Services.AddSingleton<IToolAuthorizationHandler, StubToolAuthorizationHandler>();
                break;

            case TraconProductionRisk.UnrestrictedTenantEgress:
                builder.Services.Configure<TraconTenantProviderOptions>(static options => options.RequireEgressPolicy = true);
                break;

            case TraconProductionRisk.UnverifiedTenantHeader:
                // Nothing to answer: no check carries this decision unless UseTenancy was called, and a
                // host that never opened the header path has no header to verify.
                break;

            case TraconProductionRisk.UnlimitedTenantWorkflows:
                builder.UseWorkflows(static options => options.MaxConcurrentRunsPerTenant = 2);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(risk), risk, "Unknown production risk.");
        }
    }

    /// <summary>
    /// Composes what a decision needs in order to be ASKED. Most decisions are open on the bare
    /// defaults, so they need nothing; two are not asked at all until their module is in the chain.
    /// </summary>
    private static void Open(ITraconBuilder builder, TraconProductionRisk risk)
    {
        switch (risk)
        {
            case TraconProductionRisk.UnverifiedTenantHeader:
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
                break;

            case TraconProductionRisk.UnlimitedTenantWorkflows:
                builder.UseWorkflows();
                break;
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

    private static IHost BuildHostWithoutHttp(Action<ITraconBuilder> configureTracon, RecordingLoggerProvider? logs = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
        }

        var tracon = builder.Services.AddTracon();
        tracon.AddModelProvider(new Tracon.Testing.FakeModelProvider("echo").EchoesUserMessage());
        configureTracon(tracon);

        return builder.Build();
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public string TenantId => "stub-tenant";
    }

    private sealed class StubToolAuthorizationHandler : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ToolAuthorizationResult.Allow());
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
