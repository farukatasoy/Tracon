namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IVoiceSessionStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the three SQL providers must pass the same
/// scenarios. Two rules are critical: a second write with the same ID
/// <strong>updates</strong> the record (a call is first opened, then
/// closed), and <c>input_seconds</c> does not <strong>lose</strong> its
/// decimal fraction.
/// </remarks>
public abstract class VoiceSessionStoreContract : TenantIsolationContract<IVoiceSessionStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var record = Record() with { Id = TraconId.NewId(), TenantId = tenantId, SessionId = name };
        await Store.SaveAsync(record);
        return record.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => (await Store.QueryAsync(tenantId, new VoiceSessionQuery())).Any(record => record.Id == (Guid)key);

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.QueryAsync(tenantId, new VoiceSessionQuery())).Count;

    private const string Tenant = "test";

    private static readonly DateTimeOffset Started = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Saved_call_is_read_back()
    {
        var record = Record();

        await Store.SaveAsync(record);

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.Id.ShouldBe(record.Id);
        loaded.SessionId.ShouldBe("session-1");
        loaded.AgentName.ShouldBe("support");
        loaded.Turns.ShouldBe(3);
        loaded.OutputChars.ShouldBe(420);
        loaded.EndReason.ShouldBe(VoiceSessionEndReason.Client);
        loaded.CreatedBy.ShouldBe("operator@example");
    }

    [Fact]
    public async Task Duration_does_NOT_lose_its_decimal_fraction()
    {
        // 🚨 The measurement column carries a decimal. An untyped parameter
        // is treated as decimal(18,0) on SQL Server and the fraction was
        // SILENTLY truncated.
        await Store.SaveAsync(Record() with { InputSeconds = 12.345m });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.InputSeconds.ShouldBe(12.345m);
    }

    [Fact]
    public async Task Missing_measurement_stays_null_NOT_zero()
    {
        // Tracon never fabricates a measurement (K-032): if the provider
        // did not report a duration, the field stays empty.
        await Store.SaveAsync(Record() with { InputSeconds = null, OutputChars = null, EndedAt = null });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.InputSeconds.ShouldBeNull();
        loaded.OutputChars.ShouldBeNull();
        loaded.EndedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Live_session_provider_model_and_cost_round_trip()
    {
        await Store.SaveAsync(Record() with
        {
            Provider = "openai",
            Model = "gpt-live-1",
            LiveSeconds = 28.500m,
            Cost = new VoiceSessionCost
            {
                DurationCost = 0.00475000m,
                CharacterCost = null,
                Currency = "USD",
            },
        });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.Provider.ShouldBe("openai");
        loaded.Model.ShouldBe("gpt-live-1");
        loaded.LiveSeconds.ShouldBe(28.500m);
        loaded.Cost.ShouldNotBeNull();
        loaded.Cost.DurationCost.ShouldBe(0.00475000m);
        loaded.Cost.CharacterCost.ShouldBeNull();
        loaded.Cost.Currency.ShouldBe("USD");
        loaded.Cost.Total().ShouldBe(0.00475000m);
    }

    [Fact]
    public async Task Unpriced_live_session_reads_back_with_a_null_cost_NOT_a_zero()
    {
        // 🚨 A session nobody priced must not claim it was free. The record is
        // null, not a cost object full of zeroes.
        await Store.SaveAsync(Record() with
        {
            Provider = "openai",
            Model = "gpt-live-1",
            LiveSeconds = 28.500m,
            Cost = null,
        });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.LiveSeconds.ShouldBe(28.500m);
        loaded.Cost.ShouldBeNull();
    }

    [Fact]
    public async Task The_two_new_end_reasons_round_trip()
    {
        // Abandoned and Provider were added in phase 161 and must survive the
        // smallint round trip like the five before them.
        var abandoned = Record() with { Id = TraconId.NewId(), EndReason = VoiceSessionEndReason.Abandoned };
        var byProvider = Record() with { Id = TraconId.NewId(), EndReason = VoiceSessionEndReason.Provider };

        await Store.SaveAsync(abandoned);
        await Store.SaveAsync(byProvider);

        var loaded = await Store.QueryAsync(Tenant, new VoiceSessionQuery());

        loaded.Single(record => record.Id == abandoned.Id).EndReason
            .ShouldBe(VoiceSessionEndReason.Abandoned);
        loaded.Single(record => record.Id == byProvider.Id).EndReason
            .ShouldBe(VoiceSessionEndReason.Provider);
    }

    [Fact]
    public async Task Second_write_with_the_same_id_UPDATES_the_record()
    {
        // A call is first opened (turns = 0), then closed. If two rows were
        // created, the same call would be counted twice.
        var record = Record() with { Turns = 0, EndedAt = null, EndReason = null };

        await Store.SaveAsync(record);
        await Store.SaveAsync(record with
        {
            Turns = 5,
            EndedAt = Started.AddMinutes(4),
            EndReason = VoiceSessionEndReason.IdleTimeout,
        });

        var loaded = (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).ShouldHaveSingleItem();

        loaded.Turns.ShouldBe(5);
        loaded.EndReason.ShouldBe(VoiceSessionEndReason.IdleTimeout);
    }

    [Fact]
    public async Task Another_tenants_record_is_not_visible()
    {
        await Store.SaveAsync(Record());
        await Store.SaveAsync(Record() with { Id = TraconId.NewId(), TenantId = "other" });

        (await Store.QueryAsync(Tenant, new VoiceSessionQuery())).Count.ShouldBe(1);
        (await Store.QueryAsync("other", new VoiceSessionQuery())).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Agent_and_session_filters_work()
    {
        await Store.SaveAsync(Record());
        await Store.SaveAsync(Record() with
        {
            Id = TraconId.NewId(),
            AgentName = "researcher",
            SessionId = "session-2",
        });

        (await Store.QueryAsync(Tenant, new VoiceSessionQuery { AgentName = "support" }))
            .ShouldHaveSingleItem().SessionId.ShouldBe("session-1");

        (await Store.QueryAsync(Tenant, new VoiceSessionQuery { SessionId = "session-2" }))
            .ShouldHaveSingleItem().AgentName.ShouldBe("researcher");
    }

    [Fact]
    public async Task List_is_ordered_newest_to_oldest()
    {
        await Store.SaveAsync(Record() with { Id = TraconId.NewId(), StartedAt = Started });
        await Store.SaveAsync(Record() with { Id = TraconId.NewId(), StartedAt = Started.AddMinutes(10) });
        await Store.SaveAsync(Record() with { Id = TraconId.NewId(), StartedAt = Started.AddMinutes(5) });

        var loaded = await Store.QueryAsync(Tenant, new VoiceSessionQuery());

        loaded.Count.ShouldBe(3);
        loaded[0].StartedAt.ShouldBe(Started.AddMinutes(10));
        loaded[2].StartedAt.ShouldBe(Started);
    }

    [Fact]
    public async Task Paging_is_applied()
    {
        for (var index = 0; index < 5; index++)
        {
            await Store.SaveAsync(Record() with
            {
                Id = TraconId.NewId(),
                StartedAt = Started.AddMinutes(index),
            });
        }

        var page = await Store.QueryAsync(Tenant, new VoiceSessionQuery { Skip = 1, Take = 2 });

        page.Count.ShouldBe(2);
        page[0].StartedAt.ShouldBe(Started.AddMinutes(3));
    }

    private static VoiceSessionRecord Record()
        => new()
        {
            Id = TraconId.NewId(),
            TenantId = Tenant,
            SessionId = "session-1",
            AgentName = "support",
            StartedAt = Started,
            EndedAt = Started.AddMinutes(2),
            Turns = 3,
            InputSeconds = 7.5m,
            OutputChars = 420,
            EndReason = VoiceSessionEndReason.Client,
            CreatedBy = "operator@example",
        };
}
