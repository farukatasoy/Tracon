namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IWorkflowResponseStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The store is the authority of a single-answer workflow: two concurrent answers to one
/// request must leave exactly one claim, and two concurrent re-drives of a stopped answer must
/// leave exactly one replacement. A unit test of the endpoint cannot prove either; only the
/// store can, which is why the races are cases here.
/// </para>
/// <para>
/// The <see cref="TenantIsolationContract{TStore}.TryDeleteAsync"/> hook is wired to
/// <see cref="IWorkflowResponseStore.ReplaceAsync"/>, the one mutation of an existing record a
/// caller asks for: another tenant must not be able to abandon a record.
/// </para>
/// </remarks>
public abstract class WorkflowResponseStoreContract : TenantIsolationContract<IWorkflowResponseStore>
{
    private readonly List<(string TenantId, Guid SourceRunId)> _sources = [];

    /// <summary>Opens the run row an answer's source run id points at.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="sessionId">The run's execution session.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// In the SQL implementations <c>workflow_responses.source_run_id</c> is a foreign key into
    /// <c>runs</c>; the in-memory implementation has no such link, and the hook does nothing.
    /// </remarks>
    protected virtual ValueTask PrepareRunAsync(Guid runId, string tenantId, string sessionId) => default;

    /// <summary>Erases a run the way a data subject erasure does.</summary>
    /// <param name="runId">The run to erase.</param>
    /// <param name="tenantId">The run's tenant.</param>
    /// <returns>
    /// <see langword="true"/> when the run was erased; <see langword="false"/> when the store
    /// under test has no run table to erase from, and the case does not apply.
    /// </returns>
    protected virtual ValueTask<bool> EraseRunAsync(Guid runId, string tenantId) => new(false);

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var sourceRunId = Guid.NewGuid();
        var sessionId = $"session-{name}";

        await PrepareRunAsync(sourceRunId, tenantId, sessionId);

        // Tracked before the store call: the cancellation contract asks afterwards whether a
        // cancelled write left anything, and it can only ask about a source it knows.
        _sources.Add((tenantId, sourceRunId));

        var record = Record(tenantId, sourceRunId, sessionId, $"request-{name}", claimScope: sessionId);

        (await Store.AddAsync(record, cancellationToken)).Claimed.ShouldBeTrue();

        return record.ResponseRunId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return await Store.GetAsync((Guid)key) is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;

        var count = 0;

        foreach (var (_, sourceRunId) in _sources)
        {
            count += (await Store.ListBySourceRunAsync(sourceRunId, cancellationToken)).Count;
        }

        return count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        var replacement = Record(
            tenantId,
            await SourceAsync(tenantId, "session-replacement"),
            "session-replacement",
            "request-replacement",
            claimScope: null);

        AmbientTenant.TenantId = tenantId;

        return await Store.ReplaceAsync((Guid)key, replacement);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The listing hook makes no store call on an empty store (it lists by the sources it
    /// seeded), so the read the cancellation contract checks is a claim lookup.
    /// </remarks>
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.FindClaimAsync("session-cancelled", "request-cancelled", cancellationToken);

    [Fact]
    public async Task A_recorded_answer_is_read_back_with_every_field()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var record = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1") with
        {
            RespondedBy = "editor@example",
        };

        (await Store.AddAsync(record)).ShouldBe(new WorkflowResponseClaim { Claimed = true, Existing = null });

        AmbientTenant.TenantId = TenantA;

        var loaded = (await Store.GetAsync(record.ResponseRunId)).ShouldNotBeNull();

        loaded.ResponseRunId.ShouldBe(record.ResponseRunId);
        loaded.SourceRunId.ShouldBe(source);
        loaded.SessionId.ShouldBe("session-1");
        loaded.RequestId.ShouldBe("request-1");
        loaded.AnswerHash.ShouldBe(record.AnswerHash);
        loaded.ClaimScope.ShouldBe("session-1");
        loaded.Status.ShouldBe(WorkflowResponseStatus.Accepted);
        loaded.RespondedBy.ShouldBe("editor@example");
        loaded.RespondedAt.ShouldBe(record.RespondedAt);
        loaded.TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task A_second_answer_to_a_claimed_request_is_not_written_and_names_the_first()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var first = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");
        var second = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1", hash: Hash('b'));

        (await Store.AddAsync(first)).Claimed.ShouldBeTrue();

        var claim = await Store.AddAsync(second);

        claim.Claimed.ShouldBeFalse();
        claim.Existing.ShouldNotBeNull().ResponseRunId.ShouldBe(first.ResponseRunId);
        claim.Existing.AnswerHash.ShouldBe(first.AnswerHash);

        AmbientTenant.TenantId = TenantA;

        (await Store.GetAsync(second.ResponseRunId)).ShouldBeNull();
        (await Store.ListBySourceRunAsync(source)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Answers_without_a_claim_are_all_written()
    {
        var source = await SourceAsync(TenantA, "session-1");

        for (var i = 0; i < 3; i++)
        {
            (await Store.AddAsync(Record(TenantA, source, "session-1", "request-1", claimScope: null))).Claimed.ShouldBeTrue();
        }

        AmbientTenant.TenantId = TenantA;

        (await Store.ListBySourceRunAsync(source)).Count.ShouldBe(3);
        (await Store.FindClaimAsync("session-1", "request-1")).ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_answers_to_one_request_leave_exactly_one_claim()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var records = Enumerable.Range(0, 8)
            .Select(_ => Record(TenantA, source, "session-1", "request-1", claimScope: "session-1"))
            .ToList();

        var claims = await Task.WhenAll(records.Select(record => Store.AddAsync(record).AsTask()));

        var winner = records[claims.ToList().FindIndex(static claim => claim.Claimed)];

        claims.Count(static claim => claim.Claimed).ShouldBe(1);
        claims.Where(static claim => !claim.Claimed)
            .ShouldAllBe(claim => claim.Existing != null && claim.Existing.ResponseRunId == winner.ResponseRunId);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListBySourceRunAsync(source)).ShouldHaveSingleItem().ResponseRunId.ShouldBe(winner.ResponseRunId);
    }

    [Fact]
    public async Task A_claim_is_found_by_its_scope_and_request()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var record = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(record);

        AmbientTenant.TenantId = TenantA;

        (await Store.FindClaimAsync("session-1", "request-1")).ShouldNotBeNull().ResponseRunId.ShouldBe(record.ResponseRunId);
        (await Store.FindClaimAsync("session-1", "request-2")).ShouldBeNull();
        (await Store.FindClaimAsync("session-2", "request-1")).ShouldBeNull();
    }

    [Fact]
    public async Task Replacing_abandons_the_record_and_hands_the_claim_to_the_replacement()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var first = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");
        var replacement = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(first);

        AmbientTenant.TenantId = TenantA;

        (await Store.ReplaceAsync(first.ResponseRunId, replacement)).ShouldBeTrue();

        (await Store.GetAsync(first.ResponseRunId)).ShouldNotBeNull().Status.ShouldBe(WorkflowResponseStatus.Abandoned);
        (await Store.GetAsync(replacement.ResponseRunId)).ShouldNotBeNull().Status.ShouldBe(WorkflowResponseStatus.Accepted);
        (await Store.FindClaimAsync("session-1", "request-1")).ShouldNotBeNull().ResponseRunId.ShouldBe(replacement.ResponseRunId);

        // The abandoned record is history: the source still lists both.
        (await Store.ListBySourceRunAsync(source)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_delivered_record_can_be_replaced_too()
    {
        // A run that delivered the answer and then failed before it wrote a checkpoint of its
        // own leaves nothing to resume; the same answer re-drives it from the source.
        var source = await SourceAsync(TenantA, "session-1");
        var first = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(first);

        AmbientTenant.TenantId = TenantA;

        await Store.MarkDeliveredAsync(first.ResponseRunId);

        (await Store.ReplaceAsync(first.ResponseRunId, Record(TenantA, source, "session-1", "request-1", claimScope: "session-1")))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task An_abandoned_record_cannot_be_replaced_again_and_nothing_is_written()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var first = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");
        var second = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");
        var third = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(first);

        AmbientTenant.TenantId = TenantA;

        (await Store.ReplaceAsync(first.ResponseRunId, second)).ShouldBeTrue();
        (await Store.ReplaceAsync(first.ResponseRunId, third)).ShouldBeFalse();

        (await Store.GetAsync(third.ResponseRunId)).ShouldBeNull();
        (await Store.FindClaimAsync("session-1", "request-1")).ShouldNotBeNull().ResponseRunId.ShouldBe(second.ResponseRunId);
    }

    [Fact]
    public async Task Replacing_an_unknown_record_writes_nothing()
    {
        var replacement = Record(TenantA, await SourceAsync(TenantA, "session-1"), "session-1", "request-1", claimScope: "session-1");

        AmbientTenant.TenantId = TenantA;

        (await Store.ReplaceAsync(Guid.NewGuid(), replacement)).ShouldBeFalse();
        (await Store.GetAsync(replacement.ResponseRunId)).ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_replacements_of_one_record_leave_exactly_one_claim()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var first = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(first);

        AmbientTenant.TenantId = TenantA;

        var replacements = Enumerable.Range(0, 8)
            .Select(_ => Record(TenantA, source, "session-1", "request-1", claimScope: "session-1"))
            .ToList();

        var results = await Task.WhenAll(
            replacements.Select(replacement => Store.ReplaceAsync(first.ResponseRunId, replacement).AsTask()));

        results.Count(static replaced => replaced).ShouldBe(1);

        var winner = replacements[Array.IndexOf(results, true)];

        (await Store.FindClaimAsync("session-1", "request-1")).ShouldNotBeNull().ResponseRunId.ShouldBe(winner.ResponseRunId);
        (await Store.ListBySourceRunAsync(source)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Marking_delivered_moves_only_an_accepted_record()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var first = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");
        var replacement = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(first);

        AmbientTenant.TenantId = TenantA;

        await Store.ReplaceAsync(first.ResponseRunId, replacement);
        await Store.MarkDeliveredAsync(first.ResponseRunId);
        await Store.MarkDeliveredAsync(replacement.ResponseRunId);

        (await Store.GetAsync(first.ResponseRunId))!.Status.ShouldBe(WorkflowResponseStatus.Abandoned);
        (await Store.GetAsync(replacement.ResponseRunId))!.Status.ShouldBe(WorkflowResponseStatus.Delivered);

        // A run with no record (a recovery continuation, a direct runner call) changes nothing.
        await Store.MarkDeliveredAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task Another_tenant_cannot_mark_a_record_delivered()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var record = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(record);

        AmbientTenant.TenantId = TenantB;
        await Store.MarkDeliveredAsync(record.ResponseRunId);

        AmbientTenant.TenantId = TenantA;
        (await Store.GetAsync(record.ResponseRunId))!.Status.ShouldBe(WorkflowResponseStatus.Accepted);
    }

    [Fact]
    public async Task Two_tenants_claim_the_same_scope_and_request_without_seeing_each_other()
    {
        var sourceA = await SourceAsync(TenantA, "session-1");
        var sourceB = await SourceAsync(TenantB, "session-1");
        var recordA = Record(TenantA, sourceA, "session-1", "request-1", claimScope: "session-1");
        var recordB = Record(TenantB, sourceB, "session-1", "request-1", claimScope: "session-1");

        (await Store.AddAsync(recordA)).Claimed.ShouldBeTrue();
        (await Store.AddAsync(recordB)).Claimed.ShouldBeTrue();

        AmbientTenant.TenantId = TenantA;
        (await Store.FindClaimAsync("session-1", "request-1"))!.ResponseRunId.ShouldBe(recordA.ResponseRunId);
        (await Store.ListBySourceRunAsync(sourceB)).ShouldBeEmpty();

        AmbientTenant.TenantId = TenantB;
        (await Store.FindClaimAsync("session-1", "request-1"))!.ResponseRunId.ShouldBe(recordB.ResponseRunId);
        (await Store.GetAsync(recordA.ResponseRunId)).ShouldBeNull();
    }

    [Fact]
    public async Task The_answers_of_a_source_are_listed_oldest_first()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var later = Record(TenantA, source, "session-1", "request-2", claimScope: null) with { RespondedAt = Now.AddMinutes(5) };
        var earlier = Record(TenantA, source, "session-1", "request-1", claimScope: null) with { RespondedAt = Now };

        await Store.AddAsync(later);
        await Store.AddAsync(earlier);

        AmbientTenant.TenantId = TenantA;

        var listed = await Store.ListBySourceRunAsync(source);

        listed.Select(static record => record.ResponseRunId).ShouldBe([earlier.ResponseRunId, later.ResponseRunId]);
        (await Store.ListBySourceRunAsync(Guid.NewGuid())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Erasing_the_source_run_removes_its_answers()
    {
        var source = await SourceAsync(TenantA, "session-1");
        var record = Record(TenantA, source, "session-1", "request-1", claimScope: "session-1");

        await Store.AddAsync(record);

        if (!await EraseRunAsync(source, TenantA))
        {
            // No run table under this store: nothing cascades, and the case does not apply.
            return;
        }

        AmbientTenant.TenantId = TenantA;

        (await Store.GetAsync(record.ResponseRunId)).ShouldBeNull();
        (await Store.FindClaimAsync("session-1", "request-1")).ShouldBeNull();
    }

    private static DateTimeOffset Now => new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static string Hash(char digit) => new(digit, 64);

    private static WorkflowResponseRecord Record(
        string tenantId,
        Guid sourceRunId,
        string sessionId,
        string requestId,
        string? claimScope,
        string? hash = null)
        => new()
        {
            ResponseRunId = Guid.NewGuid(),
            SourceRunId = sourceRunId,
            SessionId = sessionId,
            RequestId = requestId,
            AnswerHash = hash ?? Hash('a'),
            ClaimScope = claimScope,
            Status = WorkflowResponseStatus.Accepted,
            RespondedBy = "operator@example",
            RespondedAt = Now,
            TenantId = tenantId,
        };

    private async ValueTask<Guid> SourceAsync(string tenantId, string sessionId)
    {
        var runId = Guid.NewGuid();

        await PrepareRunAsync(runId, tenantId, sessionId);

        return runId;
    }
}
