namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IWorkflowRequestStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The store is the authority of two decisions a unit test of the runner cannot prove: which row
/// is THE open row of a logical request (the same request id in the same session held by several
/// runs), and which of an answer and an expiry reaches a row first. The first is a listing
/// question and the second is a race, so both are cases here.
/// </para>
/// <para>
/// The <see cref="TenantIsolationContract{TStore}"/> hooks are wired to
/// <see cref="IWorkflowRequestStore.AddAsync"/> and <see cref="IWorkflowRequestStore.ListAsync"/>;
/// the store offers no delete, so another tenant's chance to change a row is
/// <see cref="IWorkflowRequestStore.MarkAnsweredAsync"/>, which has a case of its own.
/// </para>
/// </remarks>
public abstract class WorkflowRequestStoreContract : TenantIsolationContract<IWorkflowRequestStore>
{
    /// <summary>Opens the run row an inbox row points at.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="sessionId">The run's execution session.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// In the SQL implementations <c>workflow_requests.run_id</c> is a foreign key into
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

    /// <summary>Gives a run the status a run has when it stops waiting, or starts to.</summary>
    /// <param name="runId">The run, opened by <see cref="PrepareRunAsync"/>.</param>
    /// <param name="tenantId">The run's tenant.</param>
    /// <param name="status">The status to give.</param>
    /// <returns>
    /// <see langword="true"/> when the run now has the status; <see langword="false"/> when the
    /// store under test has no run table, and the cases that need a waiting run do not apply.
    /// </returns>
    protected virtual ValueTask<bool> SetRunStatusAsync(Guid runId, string tenantId, RunStatus status) => new(false);

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        await PrepareRunAsync(runId, tenantId, $"session-{name}");

        await Store.AddAsync([Row(tenantId, runId, $"session-{name}", $"request-{name}", Now)], cancellationToken);

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return (await Store.ListByRunAsync((Guid)key)).Count > 0;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;

        return (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All }, cancellationToken)).Count;
    }

    [Fact]
    public async Task A_written_row_is_read_back_with_every_field()
    {
        var runId = await RunAsync(TenantA, "session-1");
        var row = Row(TenantA, runId, "session-1", "request-1", Now) with
        {
            PortId = "approval-port",
            EventSequence = 7,
            ExpiresAt = Now.AddHours(2),
        };

        await Store.AddAsync([row]);

        AmbientTenant.TenantId = TenantA;

        var loaded = (await Store.ListByRunAsync(runId)).ShouldHaveSingleItem();

        loaded.RunId.ShouldBe(runId);
        loaded.RequestId.ShouldBe("request-1");
        loaded.SessionId.ShouldBe("session-1");
        loaded.WorkflowName.ShouldBe("approval");
        loaded.PortId.ShouldBe("approval-port");
        loaded.EventSequence.ShouldBe(7);
        loaded.RequestedAt.ShouldBe(Now);
        loaded.ExpiresAt.ShouldBe(Now.AddHours(2));
        loaded.Status.ShouldBe(WorkflowRequestStatus.Open);
        loaded.AnsweredAt.ShouldBeNull();
        loaded.LastResponseRunId.ShouldBeNull();
        loaded.ClosedAt.ShouldBeNull();
        loaded.TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task A_row_without_a_time_limit_reads_back_without_one()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now)]);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListByRunAsync(runId)).ShouldHaveSingleItem().ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Writing_the_same_run_and_request_twice_keeps_the_first_row()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now) with { PortId = "first" }]);
        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now.AddMinutes(9)) with { PortId = "second" }]);

        AmbientTenant.TenantId = TenantA;

        var kept = (await Store.ListByRunAsync(runId)).ShouldHaveSingleItem();

        kept.PortId.ShouldBe("first");
        kept.RequestedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task A_run_lists_its_requests_in_event_order()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync(
        [
            Row(TenantA, runId, "session-1", "request-b", Now) with { EventSequence = 9 },
            Row(TenantA, runId, "session-1", "request-a", Now) with { EventSequence = 4 },
        ]);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListByRunAsync(runId)).Select(static row => row.RequestId).ShouldBe(["request-a", "request-b"]);
        (await Store.ListByRunAsync(Guid.NewGuid())).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_request_of_a_session_is_listed_once_under_its_newest_open_run()
    {
        var older = await RunAsync(TenantA, "session-1");
        var newer = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, older, "session-1", "request-1", Now), Row(TenantA, older, "session-1", "request-2", Now)]);
        await Store.AddAsync([Row(TenantA, newer, "session-1", "request-2", Now.AddMinutes(10))]);

        AmbientTenant.TenantId = TenantA;

        var listed = await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All });

        listed.Count.ShouldBe(2);
        listed.Single(static row => string.Equals(row.RequestId, "request-1", StringComparison.Ordinal)).RunId.ShouldBe(older);
        listed.Single(static row => string.Equals(row.RequestId, "request-2", StringComparison.Ordinal)).RunId.ShouldBe(newer);
    }

    [Fact]
    public async Task The_same_request_id_in_two_sessions_is_two_requests()
    {
        var first = await RunAsync(TenantA, "session-1");
        var second = await RunAsync(TenantA, "session-2");

        await Store.AddAsync([Row(TenantA, first, "session-1", "request-1", Now), Row(TenantA, second, "session-2", "request-1", Now)]);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_list_is_oldest_first_and_pages_without_gaps_or_repeats()
    {
        var runIds = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var runId = await RunAsync(TenantA, $"session-{i}");
            runIds.Add(runId);

            // Written out of order on purpose: the listing orders by the request's time.
            await Store.AddAsync([Row(TenantA, runId, $"session-{i}", $"request-{i}", Now.AddMinutes(4 - i))]);
        }

        AmbientTenant.TenantId = TenantA;

        var all = await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All });

        all.Select(static row => row.RequestId).ShouldBe(["request-4", "request-3", "request-2", "request-1", "request-0"]);

        var paged = new List<string>();

        for (var skip = 0; skip < 5; skip += 2)
        {
            paged.AddRange(
                (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All, Skip = skip, Take = 2 }))
                .Select(static row => row.RequestId));
        }

        paged.ShouldBe(all.Select(static row => row.RequestId));
    }

    [Fact]
    public async Task The_state_filter_runs_before_the_page_so_a_page_is_never_short()
    {
        // Seven requests, two of them answered. A filter applied after the page would return
        // a page of two with one or both rows missing; applied first, the pages hold 2, 2, 1.
        var requestIds = new List<string>();

        for (var i = 0; i < 7; i++)
        {
            var runId = await RunAsync(TenantA, $"session-{i}");

            await Store.AddAsync([Row(TenantA, runId, $"session-{i}", $"request-{i}", Now.AddMinutes(i))]);
            requestIds.Add($"request-{i}");
        }

        AmbientTenant.TenantId = TenantA;

        await Store.MarkAnsweredAsync("session-0", "request-0", Guid.NewGuid(), Now);
        await Store.MarkAnsweredAsync("session-3", "request-3", Guid.NewGuid(), Now);

        var pending = new List<string>();
        var sizes = new List<int>();

        for (var skip = 0; skip < 5; skip += 2)
        {
            var page = await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.Pending, Skip = skip, Take = 2 });

            sizes.Add(page.Count);
            pending.AddRange(page.Select(static row => row.RequestId));
        }

        sizes.ShouldBe([2, 2, 1]);
        pending.ShouldBe(["request-1", "request-2", "request-4", "request-5", "request-6"]);

        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.Answered }))
            .Select(static row => row.RequestId)
            .ShouldBe(["request-0", "request-3"]);
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).Count.ShouldBe(7);
    }

    [Fact]
    public async Task The_list_can_be_limited_to_one_workflow()
    {
        var approval = await RunAsync(TenantA, "session-1");
        var review = await RunAsync(TenantA, "session-2");

        await Store.AddAsync(
        [
            Row(TenantA, approval, "session-1", "request-1", Now),
            Row(TenantA, review, "session-2", "request-2", Now) with { WorkflowName = "review" },
        ]);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All, WorkflowName = "review" }))
            .ShouldHaveSingleItem().RequestId.ShouldBe("request-2");
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All, WorkflowName = "nothing" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_page_outside_the_range_is_cut_to_it()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now), Row(TenantA, runId, "session-1", "request-2", Now)]);

        AmbientTenant.TenantId = TenantA;

        // Take below 1 counts as 1, a negative skip as 0, and a huge take is cut to the maximum.
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All, Take = 0 })).Count.ShouldBe(1);
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All, Skip = -5, Take = 10_000 })).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Marking_answered_marks_every_open_row_of_the_logical_request_and_no_other()
    {
        var older = await RunAsync(TenantA, "session-1");
        var newer = await RunAsync(TenantA, "session-1");
        var otherSession = await RunAsync(TenantA, "session-2");

        await Store.AddAsync(
        [
            Row(TenantA, older, "session-1", "request-1", Now),
            Row(TenantA, older, "session-1", "request-2", Now),
        ]);
        await Store.AddAsync([Row(TenantA, newer, "session-1", "request-1", Now.AddMinutes(5))]);
        await Store.AddAsync([Row(TenantA, otherSession, "session-2", "request-1", Now)]);

        AmbientTenant.TenantId = TenantA;

        var answering = Guid.NewGuid();

        (await Store.MarkAnsweredAsync("session-1", "request-1", answering, Now.AddMinutes(9))).ShouldBe(2);

        var olderRows = await Store.ListByRunAsync(older);
        var marked = olderRows.Single(static row => string.Equals(row.RequestId, "request-1", StringComparison.Ordinal));

        marked.AnsweredAt.ShouldBe(Now.AddMinutes(9));
        marked.LastResponseRunId.ShouldBe(answering);
        (await Store.ListByRunAsync(newer)).ShouldHaveSingleItem().AnsweredAt.ShouldBe(Now.AddMinutes(9));

        // Another request of the same session, and the same request id in another session, stay open.
        olderRows.Single(static row => string.Equals(row.RequestId, "request-2", StringComparison.Ordinal)).AnsweredAt.ShouldBeNull();
        (await Store.ListByRunAsync(otherSession)).ShouldHaveSingleItem().AnsweredAt.ShouldBeNull();

        (await Store.MarkAnsweredAsync("session-1", "no-such-request", answering, Now)).ShouldBe(0);
    }

    [Fact]
    public async Task A_second_answer_names_its_own_run_and_keeps_the_first_time()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now)]);

        AmbientTenant.TenantId = TenantA;

        var first = Guid.NewGuid();
        var replacement = Guid.NewGuid();

        await Store.MarkAnsweredAsync("session-1", "request-1", first, Now.AddMinutes(1));
        await Store.MarkAnsweredAsync("session-1", "request-1", replacement, Now.AddMinutes(8));

        var row = (await Store.ListByRunAsync(runId)).ShouldHaveSingleItem();

        row.AnsweredAt.ShouldBe(Now.AddMinutes(1));
        row.LastResponseRunId.ShouldBe(replacement);
    }

    [Fact]
    public async Task Another_tenant_cannot_mark_a_row_answered()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now)]);

        AmbientTenant.TenantId = TenantB;

        (await Store.MarkAnsweredAsync("session-1", "request-1", Guid.NewGuid(), Now)).ShouldBe(0);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListByRunAsync(runId)).ShouldHaveSingleItem().AnsweredAt.ShouldBeNull();
    }

    [Fact]
    public async Task Two_tenants_hold_the_same_session_and_request_without_seeing_each_other()
    {
        var runA = await RunAsync(TenantA, "session-1");
        var runB = await RunAsync(TenantB, "session-1");

        await Store.AddAsync([Row(TenantA, runA, "session-1", "request-1", Now)]);
        await Store.AddAsync([Row(TenantB, runB, "session-1", "request-1", Now)]);

        AmbientTenant.TenantId = TenantA;
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldHaveSingleItem().RunId.ShouldBe(runA);
        (await Store.ListByRunAsync(runB)).ShouldBeEmpty();

        AmbientTenant.TenantId = TenantB;
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldHaveSingleItem().RunId.ShouldBe(runB);
    }

    [Fact]
    public async Task Expiry_closes_only_the_open_rows_whose_time_has_come_and_returns_them()
    {
        var due = await RunAsync(TenantA, "session-1");
        var later = await RunAsync(TenantA, "session-2");
        var unlimited = await RunAsync(TenantA, "session-3");

        await Store.AddAsync([Row(TenantA, due, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);
        await Store.AddAsync([Row(TenantA, later, "session-2", "request-2", Now) with { ExpiresAt = Now.AddHours(3) }]);
        await Store.AddAsync([Row(TenantA, unlimited, "session-3", "request-3", Now)]);

        var closed = (await Store.ExpireAsync(Now.AddHours(2), max: 100)).ShouldHaveSingleItem();

        closed.RunId.ShouldBe(due);
        closed.Status.ShouldBe(WorkflowRequestStatus.Closed);
        closed.ClosedAt.ShouldBe(Now.AddHours(2));
        closed.TenantId.ShouldBe(TenantA);

        AmbientTenant.TenantId = TenantA;

        var stored = (await Store.ListByRunAsync(due)).ShouldHaveSingleItem();

        stored.Status.ShouldBe(WorkflowRequestStatus.Closed);
        stored.ClosedAt.ShouldBe(Now.AddHours(2));
        (await Store.ListByRunAsync(later)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Open);
        (await Store.ListByRunAsync(unlimited)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Open);
    }

    [Fact]
    public async Task A_row_expires_at_its_time_and_not_before()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);

        (await Store.ExpireAsync(Now.AddHours(1).AddMilliseconds(-1), max: 100)).ShouldBeEmpty();
        (await Store.ExpireAsync(Now.AddHours(1), max: 100)).ShouldHaveSingleItem().RunId.ShouldBe(runId);
    }

    [Fact]
    public async Task A_row_is_closed_once()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);

        (await Store.ExpireAsync(Now.AddHours(2), max: 100)).ShouldHaveSingleItem();
        (await Store.ExpireAsync(Now.AddHours(3), max: 100)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Expiry_closes_the_rows_of_every_tenant_and_keeps_the_answer_mark()
    {
        var runA = await RunAsync(TenantA, "session-1");
        var runB = await RunAsync(TenantB, "session-1");

        await Store.AddAsync([Row(TenantA, runA, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);
        await Store.AddAsync([Row(TenantB, runB, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);

        AmbientTenant.TenantId = TenantA;
        await Store.MarkAnsweredAsync("session-1", "request-1", Guid.NewGuid(), Now.AddMinutes(1));

        var closed = await Store.ExpireAsync(Now.AddHours(2), max: 100);

        closed.Count.ShouldBe(2);
        closed.Select(static row => row.TenantId).Order(StringComparer.Ordinal).ShouldBe([TenantA, TenantB]);
        closed.Single(static row => string.Equals(row.TenantId, TenantA, StringComparison.Ordinal)).AnsweredAt.ShouldNotBeNull();
        closed.Single(static row => string.Equals(row.TenantId, TenantB, StringComparison.Ordinal)).AnsweredAt.ShouldBeNull();
    }

    [Fact]
    public async Task Expiry_closes_the_oldest_deadlines_first_and_no_more_than_max()
    {
        var runIds = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            var runId = await RunAsync(TenantA, $"session-{i}");
            runIds.Add(runId);

            await Store.AddAsync([Row(TenantA, runId, $"session-{i}", $"request-{i}", Now) with { ExpiresAt = Now.AddMinutes(30 - i) }]);
        }

        // Which two rows close is decided by the deadline; the order the statement returns them in is not.
        var first = (await Store.ExpireAsync(Now.AddHours(1), max: 2))
            .Select(static row => row.RequestId)
            .Order(StringComparer.Ordinal)
            .ToList();

        first.ShouldBe(["request-1", "request-2"]);
        (await Store.ExpireAsync(Now.AddHours(1), max: 2)).ShouldHaveSingleItem().RequestId.ShouldBe("request-0");
        (await Store.ExpireAsync(Now.AddHours(1), max: 0)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_closed_row_leaves_the_list_and_cannot_be_marked_answered()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);
        await Store.ExpireAsync(Now.AddHours(2), max: 100);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldBeEmpty();
        (await Store.MarkAnsweredAsync("session-1", "request-1", Guid.NewGuid(), Now)).ShouldBe(0);
        (await Store.ListByRunAsync(runId)).ShouldHaveSingleItem().AnsweredAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_logical_request_stays_listed_while_an_older_run_of_it_closes()
    {
        var older = await RunAsync(TenantA, "session-1");
        var newer = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, older, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);
        await Store.AddAsync([Row(TenantA, newer, "session-1", "request-1", Now.AddMinutes(50)) with { ExpiresAt = Now.AddHours(5) }]);

        (await Store.ExpireAsync(Now.AddHours(2), max: 100)).ShouldHaveSingleItem().RunId.ShouldBe(older);

        AmbientTenant.TenantId = TenantA;

        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldHaveSingleItem().RunId.ShouldBe(newer);
    }

    [Fact]
    public async Task Concurrent_expiries_close_each_row_exactly_once()
    {
        const int Rows = 12;

        for (var i = 0; i < Rows; i++)
        {
            var runId = await RunAsync(TenantA, $"session-{i}");

            await Store.AddAsync([Row(TenantA, runId, $"session-{i}", $"request-{i}", Now) with { ExpiresAt = Now.AddMinutes(1) }]);
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => Store.ExpireAsync(Now.AddHours(1), max: 100).AsTask()));

        var closed = results.SelectMany(static rows => rows).Select(static row => row.RequestId).ToList();

        closed.Count.ShouldBe(Rows);
        closed.Distinct(StringComparer.Ordinal).Count().ShouldBe(Rows);
    }

    [Fact]
    public async Task A_run_that_still_waits_after_its_last_row_closed_is_listed_as_stranded()
    {
        var stranded = await RunAsync(TenantA, "session-1");
        var otherTenant = await RunAsync(TenantB, "session-2");

        if (!await SetRunStatusAsync(stranded, TenantA, RunStatus.AwaitingInput) ||
            !await SetRunStatusAsync(otherTenant, TenantB, RunStatus.AwaitingInput))
        {
            // Rows and runs do not outlive the process together here: nothing can be stranded.
            (await Store.ListStrandedAsync(max: 100)).ShouldBeEmpty();
            return;
        }

        await Store.AddAsync([Row(TenantA, stranded, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);
        await Store.AddAsync([Row(TenantB, otherTenant, "session-2", "request-2", Now) with { ExpiresAt = Now.AddHours(1) }]);

        // Open rows are not stranded: the run waits on them.
        (await Store.ListStrandedAsync(max: 100)).ShouldBeEmpty();

        await Store.ExpireAsync(Now.AddHours(2), max: 100);

        // The process stopped here: the rows are closed, the runs still wait. Across tenants, like the expiry.
        var listed = await Store.ListStrandedAsync(max: 100);

        listed.Select(static row => row.RunId).Order().ShouldBe(new[] { stranded, otherTenant }.Order());
        listed.ShouldAllBe(static row => row.Status == WorkflowRequestStatus.Closed);
        listed.Single(row => row.RunId == stranded).TenantId.ShouldBe(TenantA);
        listed.Single(row => row.RunId == otherTenant).TenantId.ShouldBe(TenantB);
    }

    [Fact]
    public async Task A_run_that_is_closed_or_has_an_open_row_is_not_stranded()
    {
        var closedRun = await RunAsync(TenantA, "session-1");
        var openSibling = await RunAsync(TenantA, "session-2");

        if (!await SetRunStatusAsync(closedRun, TenantA, RunStatus.AwaitingInput) ||
            !await SetRunStatusAsync(openSibling, TenantA, RunStatus.AwaitingInput))
        {
            return;
        }

        await Store.AddAsync([Row(TenantA, closedRun, "session-1", "request-1", Now) with { ExpiresAt = Now.AddHours(1) }]);
        await Store.AddAsync(
        [
            Row(TenantA, openSibling, "session-2", "request-2", Now) with { ExpiresAt = Now.AddHours(1) },
            Row(TenantA, openSibling, "session-2", "request-3", Now) with { ExpiresAt = Now.AddDays(1) },
        ]);

        await Store.ExpireAsync(Now.AddHours(2), max: 100);

        // One run still has an open request; the other was closed the way the expiry service closes it.
        (await SetRunStatusAsync(closedRun, TenantA, RunStatus.Failed)).ShouldBeTrue();
        (await Store.ListStrandedAsync(max: 100)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Stranded_runs_are_listed_oldest_closure_first_and_no_more_than_max()
    {
        var runIds = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            var runId = await RunAsync(TenantA, $"session-{i}");
            runIds.Add(runId);

            if (!await SetRunStatusAsync(runId, TenantA, RunStatus.AwaitingInput))
            {
                return;
            }

            await Store.AddAsync([Row(TenantA, runId, $"session-{i}", $"request-{i}", Now) with { ExpiresAt = Now.AddMinutes(10 + i) }]);

            // Closed one round apart, so the closure order is the write order.
            await Store.ExpireAsync(Now.AddMinutes(10 + i), max: 100);
        }

        (await Store.ListStrandedAsync(max: 2)).Select(static row => row.RunId).ShouldBe([runIds[0], runIds[1]]);
        (await Store.ListStrandedAsync(max: 0)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Erasing_the_run_removes_its_rows()
    {
        var runId = await RunAsync(TenantA, "session-1");

        await Store.AddAsync([Row(TenantA, runId, "session-1", "request-1", Now)]);

        if (!await EraseRunAsync(runId, TenantA))
        {
            // No run table under this store: nothing cascades, and the case does not apply.
            return;
        }

        AmbientTenant.TenantId = TenantA;

        (await Store.ListByRunAsync(runId)).ShouldBeEmpty();
        (await Store.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.All })).ShouldBeEmpty();
    }

    private static DateTimeOffset Now => new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static WorkflowRequestRecord Row(
        string tenantId,
        Guid runId,
        string sessionId,
        string requestId,
        DateTimeOffset requestedAt)
        => new()
        {
            RunId = runId,
            RequestId = requestId,
            SessionId = sessionId,
            WorkflowName = "approval",
            PortId = "port",
            EventSequence = 3,
            RequestedAt = requestedAt,
            Status = WorkflowRequestStatus.Open,
            TenantId = tenantId,
        };

    private async ValueTask<Guid> RunAsync(string tenantId, string sessionId)
    {
        var runId = Guid.NewGuid();

        await PrepareRunAsync(runId, tenantId, sessionId);

        return runId;
    }
}
