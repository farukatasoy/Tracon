using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="IRunStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bellek ici depo ile PostgreSQL deposu ayni senaryolari gecmelidir.
/// </remarks>
public abstract class RunStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected IRunStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<IRunStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Olaylar_sira_numarasina_gore_okunur()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        for (var i = 0; i < 5; i++)
        {
            await Store.AppendEventAsync(TestData.Event(runId, i));
        }

        var sequences = new List<long>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId, fromSequence: 2))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public async Task Olay_alanlari_gidip_gelir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.ToolInvoking,
            Timestamp = DateTimeOffset.UtcNow,
            Text = "metin",
            ToolName = "get_order_status",
            ToolCallId = "call-1",
            Payload = "orderId=42",
        });

        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        var single = events.ShouldHaveSingleItem();
        single.Type.ShouldBe(RunEventType.ToolInvoking);
        single.Text.ShouldBe("metin");
        single.ToolName.ShouldBe("get_order_status");
        single.ToolCallId.ShouldBe("call-1");
        single.Payload.ShouldBe("orderId=42");
    }

    [Fact]
    public async Task Olmayan_calistirmaya_olay_eklenemez()
        => await Should.ThrowAsync<AgentPrismException>(
            async () => await Store.AppendEventAsync(TestData.Event(AgentPrismId.NewId(), 0)));

    [Fact]
    public async Task Olmayan_calistirma_sonlandirilamaz()
        => await Should.ThrowAsync<AgentPrismException>(
            async () => await Store.CompleteRunAsync(new RunCompletion
            {
                RunId = AgentPrismId.NewId(),
                Status = RunStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            }));

    [Fact]
    public async Task Olmayan_calistirmanin_olaylari_bos_doner()
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(AgentPrismId.NewId()))
        {
            events.Add(runEvent);
        }

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sonlandirma_ozeti_gunceller()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var completedAt = DateTimeOffset.UtcNow;

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = completedAt,
            EventCount = 7,
            Usage = new RunUsage { InputTokens = 10, OutputTokens = 20, TotalTokens = 30 },
            Error = new RunError { Type = "System.InvalidOperationException", Message = "patladi" },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Status.ShouldBe(RunStatus.Failed);
        record.EventCount.ShouldBe(7);
        record.CompletedAt.ShouldNotBeNull();
        record.Usage.ShouldNotBeNull();
        record.Usage.InputTokens.ShouldBe(10);
        record.Usage.OutputTokens.ShouldBe(20);
        record.Usage.TotalTokens.ShouldBe(30);
        record.Error.ShouldNotBeNull();
        record.Error.Type.ShouldBe("System.InvalidOperationException");
        record.Error.Message.ShouldBe("patladi");
    }

    [Fact]
    public async Task Token_bilgisi_yoksa_null_kalir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        (await Store.GetRunAsync(runId))!.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task Sorgu_agent_adina_gore_filtreler()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "beta"));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var results = await Store.QueryRunsAsync(new RunQuery { AgentName = "alpha" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static run => run.AgentName == "alpha");
    }

    [Fact]
    public async Task Sorgu_duruma_gore_filtreler()
    {
        var completed = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(completed));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = completed,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var results = await Store.QueryRunsAsync(new RunQuery { Status = RunStatus.Completed });

        results.ShouldHaveSingleItem().Id.ShouldBe(completed);
    }

    [Fact]
    public async Task Sorgu_en_yeniden_eskiye_siralar()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-10) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-5) });

        var results = await Store.QueryRunsAsync(new RunQuery());

        results.Count.ShouldBe(3);
        results[0].StartedAt.ShouldBe(now, TimeSpan.FromMilliseconds(1));
        results[^1].StartedAt.ShouldBe(now.AddMinutes(-10), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Sorgu_sayfalama_uygular()
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-i) });
        }

        var page = await Store.QueryRunsAsync(new RunQuery { Skip = 1, Take = 2 });

        page.Count.ShouldBe(2);
        page[0].StartedAt.ShouldBe(now.AddMinutes(-1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Sorgu_baslangic_zamanina_gore_filtreler()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-30) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now });

        var results = await Store.QueryRunsAsync(new RunQuery { StartedAfter = now.AddMinutes(-10) });

        results.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Olmayan_calistirma_null_doner()
        => (await Store.GetRunAsync(AgentPrismId.NewId())).ShouldBeNull();
}
