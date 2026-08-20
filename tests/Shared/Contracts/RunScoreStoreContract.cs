namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IRunScoreStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the three SQL providers must pass the same
/// scenarios. Critical rule: when the same author scores the same target (a
/// run or a message) a second time, the row is <strong>updated</strong>, not
/// duplicated -- this rule does not apply when the author is empty (an
/// anonymous setup).
/// </remarks>
public abstract class RunScoreStoreContract : TenantIsolationContract<IRunScoreStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// A score is attached to a run; the distinguishing key is the run id.
    /// Two tenants use the same run id: if isolation leaked, tenant B would
    /// see tenant A's score.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var saved = await Store.UpsertAsync(Score(IsolationRunId) with { TenantId = tenantId, Author = name });
        return saved.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => (await Store.ListAsync(tenantId, IsolationRunId)).Any(score => score.Id == (Guid)key);

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId, IsolationRunId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (Guid)key);

    private static readonly Guid IsolationRunId = AgentPrismId.NewId();

    private const string Tenant = "test";

    private static readonly DateTimeOffset Created = new(2026, 8, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Written_score_is_read_back()
    {
        var runId = AgentPrismId.NewId();
        var score = Score(runId);

        var saved = await Store.UpsertAsync(score);

        saved.Id.ShouldNotBe(Guid.Empty);

        var loaded = (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();

        loaded.RunId.ShouldBe(runId);
        loaded.Kind.ShouldBe(RunScoreKind.Binary);
        loaded.Value.ShouldBe(1);
        loaded.Comment.ShouldBe("correct answer");
        loaded.Source.ShouldBe("human");
        loaded.Author.ShouldBe("operator@example");
    }

    [Fact]
    public async Task Score_with_no_message_id_belongs_to_the_whole_run()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { MessageId = null });

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem().MessageId.ShouldBeNull();
    }

    [Fact]
    public async Task Same_author_scoring_the_same_target_twice_UPDATES_the_row()
    {
        var runId = AgentPrismId.NewId();

        var first = await Store.UpsertAsync(Score(runId) with { Value = 0 });
        var second = await Store.UpsertAsync(Score(runId) with { Value = 1, Comment = "updated" });

        second.Id.ShouldBe(first.Id);

        var loaded = (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
        loaded.Value.ShouldBe(1);
        loaded.Comment.ShouldBe("updated");
    }

    [Fact]
    public async Task Different_authors_score_the_same_target_independently()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Author = "alice" });
        await Store.UpsertAsync(Score(runId) with { Author = "bob", Value = 0 });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Different_messages_are_scored_independently()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { MessageId = "msg-1" });
        await Store.UpsertAsync(Score(runId) with { MessageId = "msg-2", Value = 0 });
        await Store.UpsertAsync(Score(runId) with { MessageId = null, Value = 0 });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Empty_author_makes_EVERY_call_open_a_new_row()
    {
        // With an anonymous setup (author null), the uniqueness rule does not
        // apply -- open question 4 (docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md).
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Author = null });
        await Store.UpsertAsync(Score(runId) with { Author = null, Value = 0 });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Another_tenants_score_is_not_visible()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId));
        await Store.UpsertAsync(Score(runId) with { TenantId = "other", Author = "other-author" });

        (await Store.ListAsync(Tenant, runId)).Count.ShouldBe(1);
        (await Store.ListAsync("other", runId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Score_is_deleted()
    {
        var runId = AgentPrismId.NewId();
        var saved = await Store.UpsertAsync(Score(runId));

        var deleted = await Store.DeleteAsync(Tenant, saved.Id);

        deleted.ShouldBeTrue();
        (await Store.ListAsync(Tenant, runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_tenants_score_cannot_be_deleted()
    {
        var runId = AgentPrismId.NewId();
        var saved = await Store.UpsertAsync(Score(runId));

        var deleted = await Store.DeleteAsync("other", saved.Id);

        deleted.ShouldBeFalse();
        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deleting_a_nonexistent_score_returns_false()
        => (await Store.DeleteAsync(Tenant, AgentPrismId.NewId())).ShouldBeFalse();

    private static RunScore Score(Guid runId)
        => new()
        {
            TenantId = Tenant,
            RunId = runId,
            MessageId = "msg-1",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Comment = "correct answer",
            Source = "human",
            Author = "operator@example",
            CreatedAt = Created,
        };
}
