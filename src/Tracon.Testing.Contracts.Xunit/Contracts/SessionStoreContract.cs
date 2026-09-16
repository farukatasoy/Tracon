
namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="ISessionStore"/> contract.
/// </summary>
/// <remarks>
/// Session state is opaque; the store must return the content back exactly, without interpreting it.
/// </remarks>
public abstract class SessionStoreContract : TenantIsolationContract<ISessionStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// The tenant is not a parameter in the interface; it is read from
    /// <see cref="ITenantContext"/>. That is why every hook sets the current
    /// tenant first.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;
        await Store.SaveAsync(TestData.Session(name), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.GetAsync((string)key) is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.QueryAsync(new SessionQuery(), cancellationToken)).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.DeleteAsync((string)key);
    }

    [Fact]
    public async Task Session_state_round_trips_without_corruption()
    {
        var state = TestData.State(
            """
            {"messages":[{"role":"user","text":"hello café naïve"},{"role":"assistant","text":"hi"}],
             "nested":{"deep":{"value":3.14159}},"flag":true,"nothing":null}
            """);

        await Store.SaveAsync(TestData.Session("s1", state));

        var loaded = await Store.GetAsync("s1");

        loaded.ShouldNotBeNull();
        loaded.State.GetRawText().ShouldBe(state.GetRawText());
        loaded.State.GetProperty("nested").GetProperty("deep").GetProperty("value").GetDouble().ShouldBe(3.14159);
        loaded.State.GetProperty("messages")[0].GetProperty("text").GetString().ShouldBe("hello café naïve");
    }

    [Fact]
    public async Task StateSchemaVersion_and_StateMafVersion_round_trip()
    {
        // Phase 126: the store carries these two fields through verbatim; it
        // does not compute or validate them (AgentSessionManager does).
        await Store.SaveAsync(TestData.Session("stamped") with
        {
            StateSchemaVersion = 2,
            StateMafVersion = "1.2.3",
        });

        var loaded = (await Store.GetAsync("stamped")).ShouldNotBeNull();

        loaded.StateSchemaVersion.ShouldBe(2);
        loaded.StateMafVersion.ShouldBe("1.2.3");
    }

    [Fact]
    public async Task A_session_saved_without_StateMafVersion_reads_back_null()
    {
        // Simulates a row written before this field existed: nothing sets
        // StateMafVersion, so it must round-trip as null, not as an empty
        // string or a default placeholder.
        await Store.SaveAsync(TestData.Session("unstamped"));

        var loaded = (await Store.GetAsync("unstamped")).ShouldNotBeNull();

        loaded.StateMafVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Saving_with_the_same_id_overwrites_the_record_and_keeps_the_creation_time()
    {
        var created = DateTimeOffset.UtcNow.AddHours(-1);

        await Store.SaveAsync(TestData.Session("s1") with
        {
            CreatedAt = created,
            UpdatedAt = created,
            State = TestData.State("""{"turn":1}"""),
        });

        var later = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Session("s1") with
        {
            CreatedAt = later,
            UpdatedAt = later,
            State = TestData.State("""{"turn":2}"""),
        });

        var loaded = await Store.GetAsync("s1");

        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(2);
        loaded.CreatedAt.ShouldBe(created, TimeSpan.FromMilliseconds(1));
        loaded.UpdatedAt.ShouldBe(later, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Delete_removes_the_record()
    {
        await Store.SaveAsync(TestData.Session("s1"));

        (await Store.DeleteAsync("s1")).ShouldBeTrue();
        (await Store.GetAsync("s1")).ShouldBeNull();
        (await Store.DeleteAsync("s1")).ShouldBeFalse();
    }

    [Fact]
    public async Task Nonexistent_session_returns_null()
        => (await Store.GetAsync("missing")).ShouldBeNull();

    [Fact]
    public async Task Query_starts_with_the_most_recently_updated()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.SaveAsync(TestData.Session("old") with { UpdatedAt = now.AddMinutes(-10) });
        await Store.SaveAsync(TestData.Session("new") with { UpdatedAt = now });
        await Store.SaveAsync(TestData.Session("mid") with { UpdatedAt = now.AddMinutes(-5) });

        var results = await Store.QueryAsync(new SessionQuery());

        results.Select(static session => session.Id).ShouldBe(["new", "mid", "old"]);
    }

    [Fact]
    public async Task Query_filters_by_agent_name()
    {
        await Store.SaveAsync(TestData.Session("s1") with { AgentName = "alpha" });
        await Store.SaveAsync(TestData.Session("s2") with { AgentName = "beta" });

        var results = await Store.QueryAsync(new SessionQuery { AgentName = "alpha" });

        results.ShouldHaveSingleItem().Id.ShouldBe("s1");
    }

    [Fact]
    public async Task Query_applies_paging()
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await Store.SaveAsync(TestData.Session($"s{i}") with { UpdatedAt = now.AddMinutes(-i) });
        }

        var page = await Store.QueryAsync(new SessionQuery { Skip = 1, Take = 2 });

        page.Select(static session => session.Id).ShouldBe(["s1", "s2"]);
    }

    [Fact]
    public async Task TryCreateAsync_returns_true_and_saves_for_a_new_id()
    {
        var record = TestData.Session("new") with { State = TestData.State("""{"turn":1}""") };

        (await Store.TryCreateAsync(record)).ShouldBeTrue();

        var loaded = await Store.GetAsync("new");
        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TryCreateAsync_returns_false_and_does_not_overwrite_for_an_existing_id()
    {
        await Store.SaveAsync(TestData.Session("existing") with { State = TestData.State("""{"turn":1}""") });

        var created = await Store.TryCreateAsync(
            TestData.Session("existing") with { State = TestData.State("""{"turn":2}""") });

        created.ShouldBeFalse();

        var loaded = await Store.GetAsync("existing");
        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task TryCreateAsync_only_one_concurrent_call_with_the_same_id_wins()
    {
        // HATA-004: in the check-then-create race, two concurrent first
        // requests generated different conversation IDs for the same NEW
        // session. TryCreateAsync must be atomic: of N concurrent calls,
        // exactly one must win.
        const int Concurrency = 8;

        var attempts = Enumerable.Range(0, Concurrency)
            .Select(i => Store.TryCreateAsync(
                    TestData.Session("race") with { State = TestData.State($$"""{"turn":{{i}}}""") })
                .AsTask());

        var results = await Task.WhenAll(attempts);

        results.Count(static won => won).ShouldBe(1);

        var loaded = await Store.GetAsync("race");
        loaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task TryCreateAsync_same_id_wins_independently_in_two_tenants()
    {
        // The primary key is (tenant_id, id) (K-018); a uniqueness violation
        // must look at the tenant+ID pair, not the ID alone.
        (await Store.TryCreateAsync(TestData.Session("shared-id") with { TenantId = TenantA })).ShouldBeTrue();
        (await Store.TryCreateAsync(TestData.Session("shared-id") with { TenantId = TenantB })).ShouldBeTrue();
    }

    [Fact]
    public async Task TryCreateAsync_stamps_the_first_version()
    {
        (await Store.TryCreateAsync(TestData.Session("versioned"))).ShouldBeTrue();

        var loaded = await Store.GetAsync("versioned");

        loaded.ShouldNotBeNull();
        loaded.Version.ShouldBe(1, "the first stored generation is 1, so a caller has something to compare against");
    }

    [Fact]
    public async Task TryUpdateAsync_replaces_the_record_and_advances_the_version()
    {
        await Store.TryCreateAsync(TestData.Session("update-me") with { State = TestData.State("""{"turn":1}""") });

        var loaded = (await Store.GetAsync("update-me")).ShouldNotBeNull();

        (await Store.TryUpdateAsync(
            TestData.Session("update-me") with { State = TestData.State("""{"turn":2}""") },
            loaded.Version)).ShouldBeTrue();

        var updated = (await Store.GetAsync("update-me")).ShouldNotBeNull();

        updated.State.GetProperty("turn").GetInt32().ShouldBe(2);
        updated.Version.ShouldBe(loaded.Version + 1);
    }

    [Fact]
    public async Task TryUpdateAsync_refuses_a_stale_version_and_leaves_the_record_alone()
    {
        await Store.TryCreateAsync(TestData.Session("stale") with { State = TestData.State("""{"turn":1}""") });

        var stale = (await Store.GetAsync("stale")).ShouldNotBeNull();

        // Somebody else writes first.
        await Store.TryUpdateAsync(
            TestData.Session("stale") with { State = TestData.State("""{"turn":2}""") },
            stale.Version);

        (await Store.TryUpdateAsync(
            TestData.Session("stale") with { State = TestData.State("""{"turn":99}""") },
            stale.Version)).ShouldBeFalse();

        var loaded = (await Store.GetAsync("stale")).ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetInt32().ShouldBe(2, "the stale write must not land");
    }

    [Fact]
    public async Task TryUpdateAsync_returns_false_for_an_id_that_does_not_exist()
        => (await Store.TryUpdateAsync(TestData.Session("never-created"), expectedVersion: 1)).ShouldBeFalse();

    [Fact]
    public async Task TryUpdateAsync_only_one_concurrent_call_from_the_same_version_wins()
    {
        // 🚨 The sibling of TryCreateAsync_only_one_concurrent_call_with_the_same_id_wins,
        // and the half that stayed open after HATA-004: the FIRST write was
        // made atomic, every later write was not. Two concurrent turns on an
        // EXISTING session both reported success and one was silently
        // overwritten. Of N concurrent updates from the same generation,
        // exactly one must win.
        const int Concurrency = 8;

        await Store.TryCreateAsync(TestData.Session("update-race"));

        var version = (await Store.GetAsync("update-race")).ShouldNotBeNull().Version;

        var attempts = Enumerable.Range(0, Concurrency)
            .Select(i => Store.TryUpdateAsync(
                    TestData.Session("update-race") with { State = TestData.State($$"""{"turn":{{i}}}""") },
                    version)
                .AsTask());

        var results = await Task.WhenAll(attempts);

        results.Count(static won => won).ShouldBe(1);
        (await Store.GetAsync("update-race")).ShouldNotBeNull().Version.ShouldBe(version + 1);
    }

    [Fact]
    public async Task SaveAsync_advances_the_version_so_a_stale_update_cannot_match_it()
    {
        // SaveAsync does not check the caller's generation. If it let the
        // caller's value land, a holder of the OLD version could still match
        // and overwrite what SaveAsync just wrote.
        await Store.TryCreateAsync(TestData.Session("unconditional"));

        var before = (await Store.GetAsync("unconditional")).ShouldNotBeNull().Version;

        await Store.SaveAsync(TestData.Session("unconditional") with { State = TestData.State("""{"turn":2}""") });

        (await Store.GetAsync("unconditional")).ShouldNotBeNull().Version.ShouldBeGreaterThan(before);

        (await Store.TryUpdateAsync(
            TestData.Session("unconditional") with { State = TestData.State("""{"turn":99}""") },
            before)).ShouldBeFalse();
    }

    [Fact]
    public async Task TryUpdateAsync_cannot_reach_another_tenants_record()
    {
        // The key is (tenant_id, id), so the same id lives independently in
        // two tenants. A conditional update must be filtered by tenant like
        // every other read and write; without that filter, one tenant could
        // overwrite another tenant's session by guessing its generation —
        // which for a fresh record is simply 1.
        (await Store.TryCreateAsync(
            TestData.Session("shared-update-id") with { State = TestData.State("""{"owner":"a"}"""), TenantId = TenantA }))
            .ShouldBeTrue();

        (await Store.TryUpdateAsync(
            TestData.Session("shared-update-id") with
            {
                State = TestData.State("""{"owner":"b"}"""),
                TenantId = TenantB,
            },
            expectedVersion: 1)).ShouldBeFalse();

        AmbientTenant.TenantId = TenantA;

        var loaded = (await Store.GetAsync("shared-update-id")).ShouldNotBeNull();
        loaded.State.GetProperty("owner").GetString().ShouldBe("a");
    }

    [Fact]
    public async Task TryUpdateAsync_writes_the_records_own_tenant_not_the_ambient_one()
    {
        // The same rule TryCreateAsync and SaveAsync follow: a record that
        // carries its own tenant wins over the ambient one, because a
        // scheduled job legitimately writes on behalf of a tenant that is not
        // ambient on the calling thread.
        (await Store.TryCreateAsync(
            TestData.Session("job-owned") with { State = TestData.State("""{"turn":1}"""), TenantId = TenantB }))
            .ShouldBeTrue();

        AmbientTenant.TenantId = TenantA;

        (await Store.TryUpdateAsync(
            TestData.Session("job-owned") with
            {
                State = TestData.State("""{"turn":2}"""),
                TenantId = TenantB,
            },
            expectedVersion: 1)).ShouldBeTrue();

        AmbientTenant.TenantId = TenantB;

        (await Store.GetAsync("job-owned")).ShouldNotBeNull()
            .State.GetProperty("turn").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task An_owned_session_round_trips_its_owner()
    {
        // Phase 148. OwnerId is the user a session belongs to - a SECOND
        // boundary under the tenant, not the owning TENANT that
        // GetOwnerTenantIdAsync answers.
        await Store.SaveAsync(TestData.Session("owned") with { OwnerId = "user-a" });

        (await Store.GetAsync("owned")).ShouldNotBeNull().OwnerId.ShouldBe("user-a");

        // And the listing carries it too, not only the single read.
        (await Store.QueryAsync(new SessionQuery()))
            .ShouldHaveSingleItem().OwnerId.ShouldBe("user-a");
    }

    [Fact]
    public async Task A_session_saved_without_an_owner_reads_back_null()
    {
        // Every row written before ownership existed, and every row written by
        // a setup that leaves it off. Null must survive as null, not become an
        // empty string.
        await Store.SaveAsync(TestData.Session("unowned"));

        (await Store.GetAsync("unowned")).ShouldNotBeNull().OwnerId.ShouldBeNull();
    }

    [Fact]
    public async Task Query_filters_by_owner_and_never_returns_an_unowned_row()
    {
        await Store.SaveAsync(TestData.Session("a1") with { OwnerId = "user-a" });
        await Store.SaveAsync(TestData.Session("b1") with { OwnerId = "user-b" });
        await Store.SaveAsync(TestData.Session("nobody"));

        var mine = await Store.QueryAsync(new SessionQuery { OwnerId = "user-a" });

        mine.ShouldHaveSingleItem().Id.ShouldBe(
            "a1",
            "an owner filter returns that owner's rows only - and an unowned row is NOBODY's, " +
            "so it must not fall into anyone's list");

        // No filter still lists everything: that is what a management listing
        // asks for, and the only way an unowned row stays reachable.
        (await Store.QueryAsync(new SessionQuery())).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Query_applies_the_owner_filter_BEFORE_paging()
    {
        // 🚨 The assertion this whole field exists for. Five owned rows and
        // five unowned ones, interleaved so the newest five are unowned. A
        // store that pages first and filters the page afterwards returns
        // FEWER than three rows here - and, worse, the gaps let a caller count
        // how many sessions other users hold.
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            // Unowned rows are the most recently updated, so they head an
            // unfiltered page.
            await Store.SaveAsync(TestData.Session($"free{i}") with { UpdatedAt = now.AddMinutes(-i) });
            await Store.SaveAsync(TestData.Session($"mine{i}") with
            {
                OwnerId = "user-a",
                UpdatedAt = now.AddMinutes(-10 - i),
            });
        }

        var page = await Store.QueryAsync(new SessionQuery { OwnerId = "user-a", Take = 3 });

        page.Count.ShouldBe(3, "paging must run over the ALREADY narrowed set");
        page.ShouldAllBe(static session => session.OwnerId == "user-a");
        page.Select(static session => session.Id).ShouldBe(["mine0", "mine1", "mine2"]);
    }

    [Fact]
    public async Task The_same_owner_stays_separate_in_two_tenants()
    {
        // Ownership is drawn UNDER the tenant and never across it: one person
        // using two tenants gets two independent data spaces, and a shared
        // owner string must not merge them.
        await Store.SaveAsync(TestData.Session("in-a") with { TenantId = TenantA, OwnerId = "same-person" });
        await Store.SaveAsync(TestData.Session("in-b") with { TenantId = TenantB, OwnerId = "same-person" });

        AmbientTenant.TenantId = TenantA;

        (await Store.QueryAsync(new SessionQuery { OwnerId = "same-person" }))
            .ShouldHaveSingleItem().Id.ShouldBe("in-a");

        AmbientTenant.TenantId = TenantB;

        (await Store.QueryAsync(new SessionQuery { OwnerId = "same-person" }))
            .ShouldHaveSingleItem().Id.ShouldBe("in-b");
    }

    [Fact]
    public async Task An_owner_at_the_maximum_allowed_length_round_trips_whole()
    {
        // 🚨 The column is bounded (SQL Server cannot index nvarchar(max), so
        // every indexed key column here is nvarchar(200)) and
        // RunLabels.MaxUserIdLength is set to exactly that bound. A value at
        // the bound must come back WHOLE - a silently truncated owner would
        // stop matching its own filter and lose the user their sessions.
        var longest = new string('u', RunLabels.MaxUserIdLength);

        await Store.SaveAsync(TestData.Session("at-the-limit") with { OwnerId = longest });

        (await Store.GetAsync("at-the-limit")).ShouldNotBeNull().OwnerId.ShouldBe(longest);

        (await Store.QueryAsync(new SessionQuery { OwnerId = longest }))
            .ShouldHaveSingleItem().Id.ShouldBe("at-the-limit");
    }

    [Fact]
    public async Task A_later_write_that_carries_no_owner_does_not_CLEAR_the_stored_one()
    {
        // 🚨 The defect class this rule closes. A session is saved more than
        // once on real paths - the queued run's approval hook saves before the
        // run's own save - and the second save can come from a worker with no
        // request behind it, carrying no owner at all. A plain assignment
        // would blank the column and drop the session out of its owner's
        // listing FOREVER, silently, while every test that only checked the
        // first write stayed green.
        await Store.TryCreateAsync(TestData.Session("keeps-owner") with { OwnerId = "user-a" });

        await Store.SaveAsync(TestData.Session("keeps-owner") with
        {
            OwnerId = null,
            State = TestData.State("""{"turn":2}"""),
        });

        var afterSave = (await Store.GetAsync("keeps-owner")).ShouldNotBeNull();
        afterSave.OwnerId.ShouldBe("user-a", "an unconditional save must not clear an owner already set");
        afterSave.State.GetProperty("turn").GetInt32().ShouldBe(2, "everything else IS overwritten");

        (await Store.TryUpdateAsync(
            TestData.Session("keeps-owner") with { OwnerId = null, State = TestData.State("""{"turn":3}""") },
            afterSave.Version)).ShouldBeTrue();

        (await Store.GetAsync("keeps-owner")).ShouldNotBeNull()
            .OwnerId.ShouldBe("user-a", "the conditional update follows the SAME rule");
    }

    [Fact]
    public async Task An_unowned_session_can_still_be_given_an_owner()
    {
        // The other side of the rule above: COALESCE keeps an owner that
        // EXISTS, it does not freeze the column at null. A row written before
        // ownership was turned on must still be claimable by a write that
        // brings one.
        await Store.TryCreateAsync(TestData.Session("adopt-me"));

        await Store.SaveAsync(TestData.Session("adopt-me") with { OwnerId = "user-a" });

        (await Store.GetAsync("adopt-me")).ShouldNotBeNull().OwnerId.ShouldBe("user-a");
    }

    [Fact]
    public async Task GetOwnerTenantIdAsync_returns_null_for_a_never_used_id()
    {
        AmbientTenant.TenantId = TenantA;
        (await Store.GetOwnerTenantIdAsync("never-used")).ShouldBeNull();
    }

    [Fact]
    public async Task GetOwnerTenantIdAsync_returns_the_real_owner_independent_of_the_ambient_tenant()
    {
        // HATA-S2-005: GetAsync is filtered by the ambient tenant, so it can
        // never correctly answer a cross-tenant ownership question.
        // GetOwnerTenantIdAsync must NOT apply a tenant filter.
        AmbientTenant.TenantId = TenantA;
        await Store.SaveAsync(TestData.Session("hidden") with { TenantId = TenantA });

        AmbientTenant.TenantId = TenantB;
        (await Store.GetOwnerTenantIdAsync("hidden")).ShouldBe(TenantA);
    }
}
