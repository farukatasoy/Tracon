using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// The rationale is the same as in ApprovalEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost SHARE a name,
// so a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Proves, at the HTTP boundary, that the audit trail's two guarantees survived phase
/// 171's move to one shared write path: the fail-closed operations still refuse when
/// the audit store rejects the write, and a best-effort one still carries on.
/// </summary>
/// <remarks>
/// <para>
/// A unit test cannot answer this. What is being checked is whether the mutation
/// reached the store, which only the real endpoint, its real DI chain and a real
/// request can show. Losing an operation's fail-closed-ness is otherwise invisible:
/// the code compiles, every other test passes, and the hole only appears the day the
/// audit store actually breaks.
/// </para>
/// <para>
/// The test host has no exception-handler middleware, so a refusal surfaces as the
/// exception propagating out of <c>SendAsync</c> rather than as a 500 response. A real
/// host answers 500; either way, the mutation did not happen.
/// </para>
/// </remarks>
public sealed class FailClosedAuditTests
{
    private const string AgentName = "approval-agent";

    [Fact]
    public async Task An_approval_decision_is_not_applied_when_its_audit_entry_cannot_be_written()
    {
        var audit = new SwitchableAuditLog();

        await using var host = await TraconTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: services =>
            {
                services.AddSingleton<IAuditLog>(audit);
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        var approvalId = await CreatePendingApprovalAsync(host);

        audit.Fail = true;

        await Should.ThrowAsync<TraconException>(async () =>
            await host.Client.PostAsJsonAsync(
                new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
                new ApprovalDecisionRequest { Approved = true }));

        // The decision was refused, not merely unrecorded: the approval is still
        // waiting for one.
        audit.Fail = false;

        using var read = await host.Client.GetAsync(
            new Uri($"/tracon/api/approvals/{approvalId}", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task The_same_approval_is_decided_once_the_audit_store_recovers()
    {
        // The refusal must be a refusal, not a poison pill: nothing about the failed
        // attempt may stop the decision from being made later.
        var audit = new SwitchableAuditLog();

        await using var host = await TraconTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: services =>
            {
                services.AddSingleton<IAuditLog>(audit);
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        var approvalId = await CreatePendingApprovalAsync(host);
        var decideUri = new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative);

        audit.Fail = true;

        await Should.ThrowAsync<TraconException>(async () =>
            await host.Client.PostAsJsonAsync(decideUri, new ApprovalDecisionRequest { Approved = true }));

        audit.Fail = false;

        using var second = await host.Client.PostAsJsonAsync(
            decideUri, new ApprovalDecisionRequest { Approved = true });

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(second)).GetProperty("status").GetString().ShouldBe("Approved");

        audit.Written.ShouldContain(entry => string.Equals(entry.Action, "approval.decision", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_in_band_approval_decision_is_refused_the_same_way_as_an_out_of_band_one()
    {
        // An approval decision can arrive two ways: through the approval inbox, or in
        // the next run's own request body. The published guarantee does not distinguish
        // them, so neither may this. Until phase 171 the in-band path was best-effort:
        // with a broken audit store it approved the call and ran the tool anyway.
        var audit = new SwitchableAuditLog();

        await using var host = await TraconTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: services => services.AddSingleton<IAuditLog>(audit));

        using var first = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "cancel the order", SessionId = "in-band-1" });

        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var requestId = await ReadApprovalRequestIdAsync(first);

        audit.Fail = true;

        using var second = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative),
            new AgentRunRequest
            {
                SessionId = "in-band-1",
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true }],
            });

        var frames = new List<SseFrame>();

        await foreach (var frame in SseReader.ReadAsync(await second.Content.ReadAsStreamAsync()))
        {
            frames.Add(frame);
        }

        // The refusal reaches the caller as the stream's error frame, carrying the same
        // sentence the out-of-band path puts in its 500.
        var error = frames.ShouldHaveSingleItem();

        error.Event.ShouldBe("error");
        error.Data.ShouldContain("TraconException");
        error.Data.ShouldContain("The approval decision for tool ");
        error.Data.ShouldContain("was not applied because it could not be written to the audit trail");

        // The decision was REFUSED, not merely unrecorded: the approved tool never ran,
        // so no frame carries its output.
        frames.ShouldAllBe(frame => !frame.Data.Contains("canceled", StringComparison.Ordinal));

        audit.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trigger_is_not_saved_when_its_audit_entry_cannot_be_written()
    {
        var audit = new SwitchableAuditLog { Fail = true };

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IAuditLog>(audit));

        await Should.ThrowAsync<TraconException>(async () =>
            await host.Client.PutAsJsonAsync(
                "/tracon/api/triggers/slack",
                new
                {
                    targetKind = "agent",
                    targetName = "demo",
                    signingSecretConfigurationName = "Tracon:TriggerSecrets:Slack",
                }));

        audit.Fail = false;

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/triggers", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(read)).EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trigger_is_not_deleted_when_its_audit_entry_cannot_be_written()
    {
        var audit = new SwitchableAuditLog();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IAuditLog>(audit));

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/triggers/slack",
            new
            {
                targetKind = "agent",
                targetName = "demo",
                signingSecretConfigurationName = "Tracon:TriggerSecrets:Slack",
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        audit.Fail = true;

        await Should.ThrowAsync<TraconException>(async () =>
            await host.Client.DeleteAsync(new Uri("/tracon/api/triggers/slack", UriKind.Relative)));

        audit.Fail = false;

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/triggers", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(read)).EnumerateArray().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_administration_call_still_succeeds_when_its_best_effort_audit_write_fails()
    {
        // The other half of the contract. Creating an agent definition writes its
        // audit row on the best-effort path; a broken audit store must cost the row,
        // not the call — and the counter is what makes that cost visible.
        using var meterFactory = new TestMeterFactory();
        using var collector = new MeterInstanceCollector(meterFactory.Meter);

        var audit = new SwitchableAuditLog { Fail = true };

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IAuditLog>(audit);
                services.AddSingleton<IMeterFactory>(meterFactory);
            });

        using var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request());

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The agent really is there: the failed audit write did not roll it back.
        using var read = await host.Client.GetAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative));

        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        var measurement = await collector.WaitForAsync(TraconDiagnostics.AuditWriteFailureCounterName);

        measurement.Value.ShouldBe(1);
        measurement.Tags[TraconDiagnostics.Tags.AuditOutcome].ShouldBe("swallowed");
        measurement.Tags[TraconDiagnostics.Tags.AuditAction].ShouldBe("agent.create");
    }

    private static void ConfigureApprovalAgent(ITraconBuilder builder)
    {
        builder
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
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            });
    }

    /// <summary>Pulls the pending approval's request id out of a synchronous run's SSE stream.</summary>
    /// <remarks>
    /// A synchronous run streams; the pending request arrives in its own <c>approvals</c>
    /// frame just before <c>done</c>. This is the only delivery path for a run started
    /// without <c>Prefer: respond-async</c>.
    /// </remarks>
    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        string? id = null;

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            if (!string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                continue;
            }

            id = System.Text.Json.JsonDocument.Parse(frame.Data)
                .RootElement.EnumerateArray()
                .Select(request => request.GetProperty("requestId").GetString())
                .FirstOrDefault();
            break;
        }

        id.ShouldNotBeNullOrEmpty("The run did not return a pending approval request to answer in band.");

        return id!;
    }

    private static async Task<Guid> CreatePendingApprovalAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "cancel the order", SessionId = "session-1" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var pending = await host.Client.GetAsync(
                new Uri("/tracon/api/approvals/pending", UriKind.Relative));

            var body = await TraconTestHost.ReadJsonAsync(pending);

            if (body.EnumerateArray().FirstOrDefault() is { ValueKind: System.Text.Json.JsonValueKind.Object } first)
            {
                return first.GetProperty("id").GetGuid();
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("The run did not produce a pending approval within the timeout.");
    }

    /// <summary>
    /// An audit log whose writes can be broken and repaired while a host is running,
    /// so one test can show both the refusal and the recovery.
    /// </summary>
    private sealed class SwitchableAuditLog : IAuditLog
    {
        private readonly List<AuditEntry> _written = [];
        private readonly Lock _gate = new();

        public bool Fail { get; set; }

        public IReadOnlyList<AuditEntry> Written
        {
            get
            {
                lock (_gate)
                {
                    return [.. _written];
                }
            }
        }

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("audit store is unreachable");
            }

            lock (_gate)
            {
                _written.Add(entry);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Written);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by these tests");
    }
}
