
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
