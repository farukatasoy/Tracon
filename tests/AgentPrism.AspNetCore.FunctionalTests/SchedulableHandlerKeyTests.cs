using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 137 §137.4 — which handler keys the HTTP schedule endpoint accepts.
/// </summary>
/// <remarks>
/// 🚨 A SECURITY boundary, not a validation nicety. A handler key is a dispatch
/// identity, so an unrestricted endpoint would turn every registered handler —
/// including the internal ones a consumer registered for its own background
/// work — into an externally callable surface. These tests exist so that
/// weakening <c>IsSchedulableOverHttp</c> to <c>return true</c> cannot stay
/// green.
/// </remarks>
public sealed class SchedulableHandlerKeyTests
{
    private static readonly Uri HandlerKeys = new("/agentprism/api/schedules/handler-keys", UriKind.Relative);
    private static readonly Uri Schedule = new("/agentprism/api/schedules/probe", UriKind.Relative);

    [Fact]
    public async Task An_unlisted_key_is_rejected_and_no_schedule_is_created()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder =>
                builder.Services.AddJobHandler<ProbeHandler>("contoso.probe"));

        using var response = await host.Client.PutAsJsonAsync(Schedule, Request("contoso.probe"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(nameof(AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys));

        using var read = await host.Client.GetAsync(Schedule);
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_built_in_key_is_accepted_by_default()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Schedule, Request(JobHandlerKeys.AgentBatch));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("handlerKey").GetString()
            .ShouldBe(JobHandlerKeys.AgentBatch);
    }

    [Fact]
    public async Task An_empty_key_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Schedule, Request(""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_allow_listed_consumer_key_is_accepted()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder =>
                builder.Services.AddJobHandler<ProbeHandler>("contoso.probe"),
            configureServices: static services => services.UseScheduling(static options =>
                options.HttpSchedulableHandlerKeys.Add("contoso.probe")));

        using var response = await host.Client.PutAsJsonAsync(Schedule, Request("contoso.probe"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_non_empty_list_REPLACES_the_built_in_default()
    {
        // The documented trade-off: an allow-list has to be able to NARROW the
        // surface, so naming only a consumer key turns the built-in keys off.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder =>
                builder.Services.AddJobHandler<ProbeHandler>("contoso.probe"),
            configureServices: static services => services.UseScheduling(static options =>
                options.HttpSchedulableHandlerKeys.Add("contoso.probe")));

        using var response = await host.Client.PutAsJsonAsync(Schedule, Request(JobHandlerKeys.AgentBatch));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_key_list_endpoint_reports_exactly_what_the_save_endpoint_accepts()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(HandlerKeys);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var keys = (await AgentPrismTestHost.ReadJsonAsync(response))
            .EnumerateArray().Select(static key => key.GetString()).ToArray();

        keys.ShouldBe([.. JobHandlerKeys.BuiltIn], ignoreOrder: true);
    }

    [Fact]
    public async Task The_key_list_endpoint_follows_the_configured_allow_list()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(static options =>
                options.HttpSchedulableHandlerKeys.Add("contoso.probe")));

        using var response = await host.Client.GetAsync(HandlerKeys);

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .EnumerateArray().Select(static key => key.GetString()).ShouldBe(["contoso.probe"]);
    }

    [Fact]
    public async Task The_key_list_endpoint_requires_Admin()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.GetAsync(HandlerKeys);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_duplicate_handler_key_stops_the_host_from_starting()
    {
        // The DI boundary of K-663: a configuration mistake breaks StartAsync,
        // not the worker's first tick (which only logs and swallows).
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => AgentPrismTestHost.StartAsync(
                configureAgentPrism: static builder =>
                {
                    builder.Services.AddJobHandler<ProbeHandler>("contoso.probe");
                    builder.Services.AddJobHandler<SecondProbeHandler>("contoso.probe");
                }));

        exception.Message.ShouldContain("contoso.probe");
    }

    private static JobScheduleSaveRequest Request(string handlerKey) => new()
    {
        HandlerKey = handlerKey,
        TargetName = "probe-target",
        TimeZone = "UTC",
        Payload = JsonDocument.Parse("[]").RootElement,
        Enabled = true,
    };

    private sealed class ProbeHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class SecondProbeHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => default;
    }
}
