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

    /// <remarks>
    /// A NULL <see cref="RunScore.MessageId"/> and an EMPTY ONE both mean "the
    /// whole run" (<see cref="Score_with_no_message_id_belongs_to_the_whole_run"/>),
    /// so a concurrent write of one shape racing a write of the OTHER shape must
    /// still collapse to a single row -- not two, one per shape.
    /// </remarks>
    [Fact]
    public async Task Concurrent_writes_with_null_and_empty_MessageId_leave_exactly_one_row()
    {
        var runId = AgentPrismId.NewId();

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(attempt =>
                Store.UpsertAsync(Score(runId) with
                {
                    MessageId = attempt % 2 == 0 ? null : string.Empty,
                    Value = attempt % 2,
                }).AsTask()));

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

    // --- SummarizeAsync (phase 154) ---
    //
    // 🚨 SummarizeAsync aggregates across a WHOLE TENANT, not one run --
    // unlike every method above, it is not scoped by a run id. Each test
    // below therefore uses its OWN tenant (never the shared `Tenant`
    // constant), or its scores would leak into (or be polluted by) another
    // test's summary.
    //
    // ByAgent and the AgentName filter are deliberately NOT tested here:
    // they resolve a score's run through a JOIN in the SQL providers, and
    // this contract has no way to seed a matching `runs` row without knowing
    // the concrete IRunStore -- that coverage lives in the functional tests
    // instead (RunScoreSummaryEndpointTests), which run the real DI-wired
    // stack.

    [Fact]
    public async Task Another_tenants_score_never_enters_the_summary()
    {
        var tenant = UniqueTenant();

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()));
        await Store.UpsertAsync(SummaryScore("other-" + tenant, AgentPrismId.NewId()));

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Different_kinds_of_the_same_name_are_not_averaged_together()
    {
        var tenant = UniqueTenant();

        // Stars (1-5) and Numeric (0-100) written under the SAME name: mixing
        // them into one average would be meaningless.
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with
        {
            Name = "quality",
            Kind = RunScoreKind.Stars,
            Value = 5,
            Author = "alice",
        });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with
        {
            Name = "quality",
            Kind = RunScoreKind.Numeric,
            Value = 80,
            Author = "bob",
        });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        summary.ByName.Count.ShouldBe(2);

        var stars = summary.ByName.Single(aggregate => aggregate.Kind == RunScoreKind.Stars);
        stars.Key.ShouldBe("quality");
        stars.Average.ShouldBe(5);

        var numeric = summary.ByName.Single(aggregate => aggregate.Kind == RunScoreKind.Numeric);
        numeric.Key.ShouldBe("quality");
        numeric.Average.ShouldBe(80);
    }

    [Fact]
    public async Task A_null_value_is_counted_but_does_not_enter_the_average()
    {
        var tenant = UniqueTenant();

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Value = 1, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Value = null, Author = "bob" });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        var aggregate = summary.ByName.ShouldHaveSingleItem();
        aggregate.Count.ShouldBe(2);
        aggregate.NoValueCount.ShouldBe(1);
        aggregate.Average.ShouldBe(1);
    }

    [Fact]
    public async Task Categorical_scores_report_category_counts_not_an_average()
    {
        var tenant = UniqueTenant();

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with
        {
            Name = "severity",
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = "minor",
            Author = "alice",
        });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with
        {
            Name = "severity",
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = "minor",
            Author = "bob",
        });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with
        {
            Name = "severity",
            Kind = RunScoreKind.Categorical,
            Value = null,
            TextValue = "major",
            Author = "carol",
        });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        var aggregate = summary.ByName.ShouldHaveSingleItem();
        aggregate.Count.ShouldBe(3);
        aggregate.NoValueCount.ShouldBe(3);
        aggregate.Average.ShouldBeNull();
        aggregate.Categories["minor"].ShouldBe(2);
        aggregate.Categories["major"].ShouldBe(1);
        aggregate.TruncatedCategoryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Category_cardinality_beyond_MaxRows_is_truncated_and_reported()
    {
        var tenant = UniqueTenant();

        for (var i = 0; i < 5; i++)
        {
            await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with
            {
                Name = "label",
                Kind = RunScoreKind.Categorical,
                Value = null,
                TextValue = $"category-{i}",
                Author = $"author-{i}",
            });
        }

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant, MaxRows = 3 });

        var aggregate = summary.ByName.ShouldHaveSingleItem();
        aggregate.Categories.Count.ShouldBe(3);
        aggregate.TruncatedCategoryCount.ShouldBe(2);
    }

    [Fact]
    public async Task Author_breakdown_excludes_scores_with_no_author()
    {
        var tenant = UniqueTenant();

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Author = null });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        summary.ByAuthor.ShouldHaveSingleItem().Key.ShouldBe("alice");
        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    [Fact]
    public async Task MessageId_is_never_a_breakdown_dimension()
    {
        var tenant = UniqueTenant();
        var runId = AgentPrismId.NewId();

        // Three DIFFERENT message ids, same score name: if MessageId leaked
        // into the grouping key, this would produce three ByName rows
        // instead of one.
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = "msg-1", Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = "msg-2", Author = "bob" });
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = "msg-3", Author = "carol" });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(3);
    }

    [Fact]
    public async Task Target_Run_excludes_message_level_scores()
    {
        var tenant = UniqueTenant();
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = null, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = "msg-1", Author = "bob" });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant, Target = RunScoreTarget.Run });

        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Target_Message_excludes_run_level_scores()
    {
        var tenant = UniqueTenant();
        var runId = AgentPrismId.NewId();

        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = null, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = "msg-1", Author = "bob" });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant, Target = RunScoreTarget.Message });

        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_string_MessageId_is_treated_the_same_as_no_message()
    {
        var tenant = UniqueTenant();
        var runId = AgentPrismId.NewId();

        // "" and null both mean "no message" -- a writer that normalizes one
        // way must not change which target bucket the score lands in.
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = string.Empty, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, runId) with { MessageId = "msg-1", Author = "bob" });

        var runOnly = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant, Target = RunScoreTarget.Run });
        var messageOnly = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant, Target = RunScoreTarget.Message });

        runOnly.ByName.ShouldHaveSingleItem().Count.ShouldBe(1);
        messageOnly.ByName.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_string_Author_is_excluded_from_the_author_breakdown_like_no_author()
    {
        var tenant = UniqueTenant();

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Author = string.Empty });

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });

        summary.ByAuthor.ShouldHaveSingleItem().Key.ShouldBe("alice");
        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_empty_match_returns_empty_lists_not_null()
    {
        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = UniqueTenant() });

        summary.ByName.ShouldBeEmpty();
        summary.ByAuthor.ShouldBeEmpty();
        summary.BySource.ShouldBeEmpty();
        summary.ByAgent.ShouldBeEmpty();
        summary.Series.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scores_are_grouped_into_time_buckets_and_empty_buckets_are_omitted()
    {
        var tenant = UniqueTenant();
        var dayOne = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        var dayTwo = new DateTimeOffset(2026, 1, 7, 3, 0, 0, TimeSpan.Zero);

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = dayOne, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = dayOne.AddHours(2), Author = "bob" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = dayTwo, Author = "carol" });

        var summary = await Store.SummarizeAsync(
            new RunScoreQuery { TenantId = tenant, Bucket = RunScoreBucket.Day });

        // A sparse series: 2026-01-06 has no score and must NOT appear.
        summary.Series.Count.ShouldBe(2);

        var expectedFirstBucket = RunScoreBucketing.Truncate(dayOne, RunScoreBucket.Day);
        var first = summary.Series.Single(bucket => bucket.BucketStart == expectedFirstBucket);
        first.Groups.ShouldHaveSingleItem().Count.ShouldBe(2);

        var expectedSecondBucket = RunScoreBucketing.Truncate(dayTwo, RunScoreBucket.Day);
        var second = summary.Series.Single(bucket => bucket.BucketStart == expectedSecondBucket);
        second.Groups.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Hour_buckets_group_scores_within_the_same_hour()
    {
        var tenant = UniqueTenant();
        var hourOne = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        var hourTwo = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = hourOne, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = hourOne.AddMinutes(45), Author = "bob" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = hourTwo, Author = "carol" });

        var summary = await Store.SummarizeAsync(
            new RunScoreQuery { TenantId = tenant, Bucket = RunScoreBucket.Hour });

        // A sparse series: the 9 AM hour has no score and must NOT appear.
        summary.Series.Count.ShouldBe(2);

        var expectedFirstBucket = RunScoreBucketing.Truncate(hourOne, RunScoreBucket.Hour);
        summary.Series.Single(bucket => bucket.BucketStart == expectedFirstBucket)
            .Groups.ShouldHaveSingleItem().Count.ShouldBe(2);

        var expectedSecondBucket = RunScoreBucketing.Truncate(hourTwo, RunScoreBucket.Hour);
        summary.Series.Single(bucket => bucket.BucketStart == expectedSecondBucket)
            .Groups.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    /// <remarks>
    /// The week bucket is the riskiest of the three: each of the four
    /// implementations (three SQL dialects plus the in-memory store) computes
    /// "the preceding Monday" with a DIFFERENT formula (see
    /// <c>RunScoreBucketing.Truncate</c>'s remarks and each provider's
    /// <c>SelectRunScoreSeries</c> query). This test is what proves they agree.
    /// </remarks>
    [Fact]
    public async Task Week_buckets_start_on_Monday_and_group_the_whole_week()
    {
        var tenant = UniqueTenant();

        // 2026-01-05 is a Monday; 2026-01-08 (Thursday) falls in the SAME ISO
        // week. 2026-01-19 (the Monday two weeks later) must land in a
        // DIFFERENT bucket, with the week in between staying empty.
        var weekOneMonday = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        var weekOneThursday = new DateTimeOffset(2026, 1, 8, 20, 0, 0, TimeSpan.Zero);
        var weekThreeMonday = new DateTimeOffset(2026, 1, 19, 8, 0, 0, TimeSpan.Zero);

        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = weekOneMonday, Author = "alice" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = weekOneThursday, Author = "bob" });
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { CreatedAt = weekThreeMonday, Author = "carol" });

        var summary = await Store.SummarizeAsync(
            new RunScoreQuery { TenantId = tenant, Bucket = RunScoreBucket.Week });

        // A sparse series: the middle week has no score and must NOT appear.
        summary.Series.Count.ShouldBe(2);

        var expectedFirstBucket = RunScoreBucketing.Truncate(weekOneMonday, RunScoreBucket.Week);
        expectedFirstBucket.ShouldBe(RunScoreBucketing.Truncate(weekOneThursday, RunScoreBucket.Week));
        summary.Series.Single(bucket => bucket.BucketStart == expectedFirstBucket)
            .Groups.ShouldHaveSingleItem().Count.ShouldBe(2);

        var expectedSecondBucket = RunScoreBucketing.Truncate(weekThreeMonday, RunScoreBucket.Week);
        summary.Series.Single(bucket => bucket.BucketStart == expectedSecondBucket)
            .Groups.ShouldHaveSingleItem().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Canceled_token_throws()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Store.SummarizeAsync(new RunScoreQuery { TenantId = UniqueTenant() }, source.Token));
    }

    [Fact]
    public async Task Summary_can_be_read_while_a_score_is_being_written()
    {
        var tenant = UniqueTenant();
        await Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Author = "alice" });

        await Task.WhenAll(
            Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant }).AsTask(),
            Store.UpsertAsync(SummaryScore(tenant, AgentPrismId.NewId()) with { Author = "bob" }).AsTask());

        var summary = await Store.SummarizeAsync(new RunScoreQuery { TenantId = tenant });
        summary.ByName.ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    private static string UniqueTenant() => $"summary-{AgentPrismId.NewId():N}";

    private static RunScore SummaryScore(string tenantId, Guid runId)
        => new()
        {
            TenantId = tenantId,
            RunId = runId,
            MessageId = null,
            Name = "helpfulness",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "operator@example",
            CreatedAt = Created,
        };

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
