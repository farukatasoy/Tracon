using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Verifies that guard decisions are written to the run record and to the audit trail.
/// </summary>
/// <remarks>
/// Tests run through the real path — via <see cref="RunRecordingAgent"/> — because the
/// event writer reads from the run scope (<see cref="AgentPrismRunContext"/>) and the
/// scope is only established there. A directly wired pipeline would never prove this chain.
/// </remarks>
public sealed class ContentGuardRecordingTests
{
    private const string CardNumber = "4539578763621486";

    [Fact]
    public async Task Masking_writes_a_ContentMasked_event()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Masking(CardNumber, "[redacted]"));

        await agent.RunAsync($"my card number is {CardNumber}");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);

        var masked = (await ReadEventsAsync(store, run.Id))
            .Where(static runEvent => runEvent.Type == RunEventType.ContentMasked)
            .ShouldHaveSingleItem();

        masked.Text.ShouldBe("stub/trigger (Input)");
    }

    [Fact]
    public async Task Event_payload_does_not_carry_the_masked_content()
    {
        // 🚨 The event reports THAT masking happened, not WHAT was masked.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Masking(CardNumber, "[redacted]"));

        await agent.RunAsync($"my card number is {CardNumber}");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        foreach (var runEvent in await ReadEventsAsync(store, run.Id))
        {
            if (runEvent.Type is RunEventType.ContentMasked or RunEventType.ContentBlocked)
            {
                (runEvent.Text ?? string.Empty).ShouldNotContain(CardNumber, Case.Sensitive);
                (runEvent.Payload ?? string.Empty).ShouldNotContain(CardNumber, Case.Sensitive);
            }
        }
    }

    [Fact]
    public async Task Blocking_fails_the_run_with_content_blocked()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Blocking("secret-project"));

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("what is secret-project"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Type.ShouldBe("content_blocked");
        run.Error.Class.ShouldBe(RunErrorClass.ContentBlocked);

        (await ReadEventsAsync(store, run.Id))
            .Where(static runEvent => runEvent.Type == RunEventType.ContentBlocked)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Blocking_is_written_to_the_audit_trail()
    {
        var auditLog = new InMemoryAuditLog();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Blocking("secret-project"), auditLog);

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("what is secret-project"));

        var entry = (await auditLog.QueryAsync(new AuditQuery { TenantId = "default" })).ShouldHaveSingleItem();

        entry.Action.ShouldBe("content.blocked");
        entry.Entity.ShouldStartWith("run:");
        entry.After.ShouldNotBeNull();
        entry.After!.ShouldContain("\"guard\":\"stub\"", Case.Sensitive);
        entry.After.ShouldContain("\"rule\":\"trigger\"", Case.Sensitive);
    }

    [Fact]
    public async Task Audit_trail_does_not_carry_the_blocked_text()
    {
        // 🚨 Blocked content is sensitive by definition. Writing it to the audit
        // trail would make the problem PERMANENT (the spirit of K-059).
        var auditLog = new InMemoryAuditLog();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Blocking("secret-project"), auditLog);

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("the product known by the code name secret-project"));

        foreach (var entry in await auditLog.QueryAsync(new AuditQuery { TenantId = "default" }))
        {
            (entry.Before ?? string.Empty).ShouldNotContain("secret-project", Case.Insensitive);
            (entry.After ?? string.Empty).ShouldNotContain("secret-project", Case.Insensitive);
            entry.Entity.ShouldNotContain("secret-project", Case.Insensitive);
        }
    }

    [Fact]
    public async Task Masking_is_not_written_to_the_audit_trail()
    {
        // Masking is written to the run event; the audit trail is reserved for
        // blocking decisions (it must stay traceable even if the run record is deleted).
        var auditLog = new InMemoryAuditLog();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Masking(CardNumber, "***"), auditLog);

        await agent.RunAsync($"card {CardNumber}");

        (await auditLog.QueryAsync(new AuditQuery { TenantId = "default" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunStarted_event_does_not_carry_the_masked_input_raw()
    {
        // HATA-S3-006: ContentGuardingChatClient masks the text sent to the
        // model, but RunRecordingAgent.BeginRunAsync used to write the
        // RunStarted event from its own RAW `messages` list BEFORE it reached
        // the model.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var pipeline = TestData.ContentGuards(guards: StubContentGuard.Masking(CardNumber, "[redacted]"));
        var agent = Agent(store, new FakeChatClient(), pipeline);

        await agent.RunAsync($"my card number is {CardNumber}");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        var started = (await ReadEventsAsync(store, run.Id))
            .Where(static runEvent => runEvent.Type == RunEventType.RunStarted)
            .ShouldHaveSingleItem();

        (started.Text ?? string.Empty).ShouldNotContain(CardNumber, Case.Sensitive);
        (started.Text ?? string.Empty).ShouldContain("[redacted]", Case.Sensitive);
    }

    [Fact]
    public async Task RunStarted_event_does_not_carry_the_blocked_input_raw()
    {
        // The same root cause applies to blocking (HATA-S3-006, MT-GUARD-043
        // scope expansion): RunStarted used to write the raw text BEFORE the
        // actual block happened on the way to the model.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var pipeline = TestData.ContentGuards(guards: StubContentGuard.Blocking("secret-project"));
        var agent = Agent(store, new FakeChatClient(), pipeline);

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("give me information about secret-project"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);

        var started = (await ReadEventsAsync(store, run.Id))
            .Where(static runEvent => runEvent.Type == RunEventType.RunStarted)
            .ShouldHaveSingleItem();

        (started.Text ?? string.Empty).ShouldNotContain("secret-project", Case.Sensitive);
    }

    [Fact]
    public async Task Guard_context_carries_the_run_id_and_the_tenant()
    {
        // A prerequisite for writing tenant-scoped rules.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var guard = StubContentGuard.Blocking("never-matches");
        var agent = Agent(store, new FakeChatClient(), guard);

        await agent.RunAsync("hello");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        guard.LastContext.ShouldNotBeNull();
        guard.LastContext!.RunId.ShouldBe(run.Id);
        guard.LastContext.TenantId.ShouldBe("default");
        guard.LastContext.AgentName.ShouldBe("test-agent");
        guard.LastContext.ModelId.ShouldBe("fake-model");
    }

    private static RunRecordingAgent Agent(
        InMemoryRunStore store,
        FakeChatClient client,
        IContentGuard guard,
        IAuditLog? auditLog = null)
        => Agent(store, client, TestData.ContentGuards(auditLog, guards: guard));

    private static RunRecordingAgent Agent(
        InMemoryRunStore store,
        FakeChatClient client,
        ContentGuardPipeline pipeline)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(pipeline, new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            FixedTenantContext.Default,
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            errorClassifier: new DefaultRunErrorClassifier(),
            contentGuardPipeline: pipeline);
    }

    private static async Task<List<RunEvent>> ReadEventsAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
