namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IRunScoreStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the three SQL providers must pass the same
/// scenarios. Critical rule: when the same author writes the same
/// <see cref="RunScore.Name"/> onto the same target (a run or a message) a
/// second time, the row is <strong>updated</strong>, not duplicated -- a
/// different name opens a new row, and neither rule applies when the author is
/// empty (an anonymous setup).
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
        // `name` is the actor name here, not the score name: the isolation
        // contract varies the author so two tenants' rows stay distinct.
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
        loaded.Name.ShouldBe("helpfulness");
        loaded.Kind.ShouldBe(RunScoreKind.Binary);
        loaded.Value.ShouldBe(1);
        loaded.TextValue.ShouldBeNull();
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
    public async Task Same_author_scoring_the_same_target_under_a_DIFFERENT_name_opens_a_new_row()
    {
        // The whole point of phase 152: one reviewer scores the same run for
        // helpfulness AND accuracy, and neither write erases the other.
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Name = "helpfulness", Value = 1 });
        await Store.UpsertAsync(Score(runId) with { Name = "accuracy", Value = 0 });

        var loaded = await Store.ListAsync(Tenant, runId);

        loaded.Count.ShouldBe(2);
        loaded.Single(score => string.Equals(score.Name, "helpfulness", StringComparison.Ordinal)).Value.ShouldBe(1);
        loaded.Single(score => string.Equals(score.Name, "accuracy", StringComparison.Ordinal)).Value.ShouldBe(0);
    }

    [Fact]
    public async Task Updating_ONE_name_leaves_the_authors_other_names_untouched()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Name = "helpfulness", Value = 1 });
        await Store.UpsertAsync(Score(runId) with { Name = "accuracy", Value = 0 });
        await Store.UpsertAsync(Score(runId) with { Name = "helpfulness", Value = 0, Comment = "changed" });

        var loaded = await Store.ListAsync(Tenant, runId);

        loaded.Count.ShouldBe(2);
        loaded.Single(score => string.Equals(score.Name, "helpfulness", StringComparison.Ordinal)).Comment.ShouldBe("changed");
        loaded.Single(score => string.Equals(score.Name, "accuracy", StringComparison.Ordinal)).Comment.ShouldBe("correct answer");
    }

    [Fact]
    public async Task A_decimal_value_is_NOT_rounded()
    {
        // The old column was `integer`; 0.87 used to become 1 -- or worse, 0.
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with
        {
            Name = "similarity",
            Kind = RunScoreKind.Numeric,
            Value = 0.87,
        });

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem().Value.ShouldBe(0.87);
    }

    [Fact]
    public async Task A_whole_number_value_is_read_back_as_a_number_not_an_integer()
    {
        // SQLite folds a lossless real into an INTEGER unless the column has
        // REAL affinity; the reader asks for a double either way.
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Kind = RunScoreKind.Numeric, Value = 4 });

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem().Value.ShouldBe(4);
    }

    [Fact]
    public async Task A_null_value_means_NO_MEASUREMENT_not_zero()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with { Kind = RunScoreKind.Numeric, Value = null });

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem().Value.ShouldBeNull();
    }

    [Fact]
    public async Task A_categorical_score_is_stored_as_text()
    {
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(Score(runId) with
        {
            Name = "severity",
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = "minor",
        });

        var loaded = (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();

        loaded.Kind.ShouldBe(RunScoreKind.Categorical);
        loaded.TextValue.ShouldBe("minor");
        loaded.Value.ShouldBeNull();
    }

    [Fact]
    public async Task A_categorical_score_carrying_a_numeric_value_is_REJECTED()
        => await Should.ThrowAsync<ArgumentException>(async () => await Store.UpsertAsync(Score(AgentPrismId.NewId()) with
        {
            Kind = RunScoreKind.Categorical,
            Value = 1,
            TextValue = "minor",
        }));

    [Fact]
    public async Task A_numeric_score_carrying_a_text_value_is_REJECTED()
        => await Should.ThrowAsync<ArgumentException>(async () => await Store.UpsertAsync(Score(AgentPrismId.NewId()) with
        {
            Kind = RunScoreKind.Numeric,
            Value = 1,
            TextValue = "minor",
        }));

    [Theory]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("naïve")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task An_illegal_name_is_REJECTED(string name)
        => await Should.ThrowAsync<ArgumentException>(
            async () => await Store.UpsertAsync(Score(AgentPrismId.NewId()) with { Name = name }));

    /// <remarks>
    /// A uniqueness key that does not match shows up first as a race: two
    /// writers each miss the other's row and both insert.
    /// </remarks>
    [Fact]
    public async Task Concurrent_writes_of_the_SAME_name_leave_exactly_one_row()
    {
        var runId = AgentPrismId.NewId();

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(attempt =>
                Store.UpsertAsync(Score(runId) with { Value = attempt % 2 }).AsTask()));

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Repeated_writes_of_the_SAME_name_leave_exactly_one_row()
    {
        // Guards the new (…, author, name) uniqueness index against the failure
        // it would show first: a key that no longer matches, so every write
        // opens a row instead of updating one.
        var runId = AgentPrismId.NewId();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Store.UpsertAsync(Score(runId) with { Value = attempt % 2 });
        }

        (await Store.ListAsync(Tenant, runId)).ShouldHaveSingleItem();
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
            Name = "helpfulness",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Comment = "correct answer",
            Source = "human",
            Author = "operator@example",
            CreatedAt = Created,
        };
}
