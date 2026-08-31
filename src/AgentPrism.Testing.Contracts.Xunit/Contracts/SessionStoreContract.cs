
namespace AgentPrism.Testing.Contracts.Storage;

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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;
        await Store.SaveAsync(TestData.Session(name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.GetAsync((string)key) is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.QueryAsync(new SessionQuery())).Count;
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
