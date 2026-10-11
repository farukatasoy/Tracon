using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A dependency that times out on its own throws an
/// <see cref="OperationCanceledException"/> while the request is still open. The
/// HTTP surfaces treat it as the failure it is, not as the caller going away.
/// </summary>
/// <remarks>
/// <para>
/// <c>HttpClient.Timeout</c> surfaces as <see cref="TaskCanceledException"/>, and
/// every public store, catalog and identity seam can be written over HTTP. Each
/// site below filtered its catch on <c>ex is not OperationCanceledException</c>, so
/// that one exception broke the site's own promise: a run failed because its
/// attribution timed out, an approved turn never ran because its "do not ask again"
/// rule could not be written, a trigger retry was refused as a replay forever, and
/// an external surface stopped the host (K-840, F-279).
/// </para>
/// <para>
/// The rule they now share is <c>OperationCancellation.IsFailure(exception, token)</c>;
/// <c>CancellationFilterSiteTests</c> keeps a new site from going back to the old
/// filter. These tests prove the behaviour at the boundary the defect crossed.
/// </para>
/// </remarks>
public sealed class ForeignCancellationEndpointTests
{
    private const string ApprovalAgent = "approval-agent";
    private const string TriggerSecretKey = "Tracon:TriggerSecrets:Slack";
    private const string TriggerSecret = "test-signing-secret";

    [Fact]
    public async Task A_run_whose_attribution_times_out_runs_without_attribution()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services =>
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new TimingOutAttribution())));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{TestData.Definition().Name}/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello", SessionId = "attribution-timeout" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var run = (await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { SessionId = "attribution-timeout" }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Completed);
        run.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task An_approved_turn_runs_when_its_remembered_rule_cannot_be_written_in_time()
    {
        // The fault is injected AFTER AddTracon: it wraps the store Tracon registered.
        await using var host = await TraconTestHost.StartAsync(static builder =>
        {
            ConfigureApprovalAgent(builder);
            builder.Services.ThrowFrom<IToolApprovalRuleStore>(
                nameof(IToolApprovalRuleStore.AddAsync),
                static () => new TaskCanceledException("the rule store timed out"));
        });

        var runUri = new Uri($"/tracon/api/agents/{ApprovalAgent}/run", UriKind.Relative);

        using var pending = await host.Client.PostAsJsonAsync(
            runUri,
            new AgentRunRequest { Message = "cancel the order", SessionId = "remember-timeout" });

        pending.StatusCode.ShouldBe(HttpStatusCode.OK);
        var requestId = await ReadApprovalRequestIdAsync(pending);

        using var answered = await host.Client.PostAsJsonAsync(
            runUri,
            new AgentRunRequest
            {
                SessionId = "remember-timeout",
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true, Remember = true }],
            });

        answered.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The approval was given; failing to remember it only means the next
        // call asks again. The turn itself runs the tool.
        (await answered.Content.ReadAsStringAsync()).ShouldContain("ORD-7 canceled.");
    }

    [Fact]
    public async Task A_trigger_whose_queueing_times_out_can_be_retried_and_is_not_taken_for_a_replay()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.Services.ThrowFrom<IJobStore>(
                nameof(IJobStore.EnqueueAsync),
                static () => new TaskCanceledException("the job store timed out"),
                times: 1),
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(TriggerSecretKey, TriggerSecret)]).Build()));

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/triggers/slack",
                   new
                   {
                       targetKind = "agent",
                       targetName = "demo",
                       signingSecretConfigurationName = TriggerSecretKey,
                       payloadMode = "wholeBody",
                       enabled = true,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        const string Body = """{"event":{"text":"hello"}}""";
        var timestamp = DateTimeOffset.UtcNow;
        var signature = WebhookSigner.Sign(Body, timestamp, TriggerSecret);

        // The first delivery fails inside the server: the test host surfaces the
        // unhandled exception to the client, which is what a 500 is there.
        await Should.ThrowAsync<TaskCanceledException>(async () =>
        {
            using var _ = await SendSignedAsync(host, Body, timestamp, signature);
        });

        // The sender retries the same delivery. The reservation the failed
        // attempt made was released, so this is accepted, not a 409 replay.
        using var retried = await SendSignedAsync(host, Body, timestamp, signature);

        retried.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task The_external_surface_check_retries_a_catalog_that_times_out_instead_of_stopping_the_host()
    {
        var catalog = new TimingOutOnceCatalog();

        var descriptors = await ExternalSurfaceGuard.ListCatalogWithRetryAsync(
            catalog,
            new RunningLifetime(),
            NullLogger.Instance,
            "MCP");

        descriptors.ShouldHaveSingleItem().Name.ShouldBe("alpha");
        catalog.Calls.ShouldBe(2);
    }

    private static void ConfigureApprovalAgent(ITraconBuilder builder)
        => builder
            .AddModelProvider(new FakeModelProvider("approval-model")
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} canceled."),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: options => options.RequiresApproval = true)
            .AddAgent(new AgentDefinition
            {
                Name = ApprovalAgent,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            });

    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            if (string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                return System.Text.Json.JsonDocument.Parse(frame.Data)
                    .RootElement.EnumerateArray()
                    .Select(request => request.GetProperty("requestId").GetString())
                    .First()
                    .ShouldNotBeNull();
            }
        }

        throw new ShouldAssertException("The run did not return a pending approval request.");
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(
        TraconTestHost host,
        string body,
        DateTimeOffset timestamp,
        string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/triggers/default/slack")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSigner.SignatureHeader, signature);

        return await host.Client.SendAsync(request);
    }

    private sealed class TimingOutAttribution : IRunAttributionContext
    {
        public string? UserId => throw new TaskCanceledException("the identity service timed out");

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>A catalog whose first listing times out on its own, as an HTTP-backed source would.</summary>
    private sealed class TimingOutOnceCatalog : IAgentCatalog
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _calls) == 1
                ? throw new TaskCanceledException("the agent source timed out")
                : new((IReadOnlyList<AgentDescriptor>)[new AgentDescriptor { Name = "alpha", Origin = AgentDefinitionOrigin.Code, SourceName = "code" }]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>An application that is running and not stopping.</summary>
    private sealed class RunningLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => throw new ShouldAssertException("The external surface check stopped the application.");
    }
}
