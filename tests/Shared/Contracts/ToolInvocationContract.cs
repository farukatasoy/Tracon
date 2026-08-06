
namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IRunStore"/> tool cagrisi sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Faz 6'da eklendi. Bellek ici depo ile PostgreSQL deposu ayni senaryolari
/// gecmelidir; ozet iki uygulamada da <em>deponun kendisinde</em> hesaplanir.
/// </remarks>
public abstract class ToolInvocationContract : TenantIsolationContract<IRunStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// Tool cagrisi bir calistirmaya asilidir; yalitim calistirmanin kiracisi
    /// uzerinden kurulur.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));
        await Store.RecordToolInvocationAsync(Invocation(runId, name, TimeSpan.FromMilliseconds(12)));

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.ListToolInvocationsAsync((Guid)key)).Count > 0;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.GetToolUsageAsync(new ToolUsageQuery())).Count;
    }

    [Fact]
    public async Task Cagri_alanlari_gidip_gelir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var invocation = new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = "get_order_status",
            ToolCallId = "call-1",
            Source = "github",
            Arguments = "orderId=ORD-1",
            Result = "kargoda",
            Duration = TimeSpan.FromMilliseconds(1234),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Store.RecordToolInvocationAsync(invocation);

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.ToolName.ShouldBe("get_order_status");
        string.Equals(stored.ToolCallId, "call-1", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(stored.Source, "github", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(stored.Arguments, "orderId=ORD-1", StringComparison.Ordinal).ShouldBeTrue();
        stored.Succeeded.ShouldBeTrue();
        stored.Duration.ShouldNotBeNull();
        stored.Duration.Value.TotalMilliseconds.ShouldBe(1234, tolerance: 1);
    }

    [Fact]
    public async Task Token_disi_olcum_gidip_gelir()
    {
        // Faz 28: ses tool'lari token degil KARAKTER veya SANIYE ile faturalanir.
        // Bes sutun (birim, miktar, tahmin, tutar, para birimi) uc diyalektte de
        // ayni sekilde gidip gelmelidir.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = "speak",
            ToolCallId = "call-ses",
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Characters,
                Quantity = 1234.5m,

                // 🚨 Ondalik kismin KESILMEDIGINI dogrular. SQL Server'da tipi
                // verilmemis bir decimal parametresi decimal(18,0) sayilir ve
                // ondalik sessizce kesilir (Faz 23 dersi).
                Cost = 0.0001357m,
                Currency = "USD",
                IsEstimated = true,
            },
        });

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Usage.ShouldNotBeNull();
        string.Equals(stored.Usage.Unit, ToolUsageUnits.Characters, StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.Quantity.ShouldBe(1234.5m);
        stored.Usage.Cost.ShouldBe(0.0001357m);
        string.Equals(stored.Usage.Currency, "USD", StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.IsEstimated.ShouldBeTrue();
    }

    [Fact]
    public async Task Olcum_bildirilmeyen_cagri_BOS_olcumle_doner()
    {
        // Cagrilarin buyuk cogunlugu olcum tasimaz. Bos bir ToolCallUsage
        // dondurmek "olculdu ama sifir" anlamina gelirdi.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "get_order_status", TimeSpan.Zero));

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Usage.ShouldBeNull();
    }

    [Fact]
    public async Task Sure_bilinmiyorsa_bos_kalir()
    {
        // Akissiz calistirmada cagri ile sonuc ayni anda gorulur; sifira yakin
        // bir sure yazmak yanlis veri uretirdi.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", duration: null));

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Duration.ShouldBeNull();
    }

    [Fact]
    public async Task Hatali_cagri_basarisiz_sayilir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(
            Invocation(runId, "tool_a", duration: TimeSpan.FromMilliseconds(5)) with
            {
                Error = "patladi",
            });

        var stored = (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();

        stored.Succeeded.ShouldBeFalse();
        string.Equals(stored.Error, "patladi", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Cagrilar_zaman_sirasina_gore_doner()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var start = DateTimeOffset.UtcNow;

        // Bilerek ters sirada yaziliyor: siralamayi depo yapmalidir.
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "ikinci", TimeSpan.Zero) with { CreatedAt = start.AddSeconds(2) });
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "birinci", TimeSpan.Zero) with { CreatedAt = start });

        var names = (await Store.ListToolInvocationsAsync(runId))
            .Select(static record => record.ToolName)
            .ToList();

        names.ShouldBe(["birinci", "ikinci"]);
    }

    [Fact]
    public async Task Ozet_tool_bazinda_toplanir()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", TimeSpan.FromMilliseconds(100)));
        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_a", TimeSpan.FromMilliseconds(300)));
        await Store.RecordToolInvocationAsync(
            Invocation(runId, "tool_a", TimeSpan.FromMilliseconds(200)) with { Error = "hata" });
        await Store.RecordToolInvocationAsync(Invocation(runId, "tool_b", TimeSpan.FromMilliseconds(50)));

        var usage = await Store.GetToolUsageAsync(new ToolUsageQuery());

        // Shouldly'nin ShouldContain(predicate) asiri yuklemesi void doner;
        // bulunan ogeyi kullanmak icin LINQ ile secilir.
        var toolA = usage
            .Single(row => string.Equals(row.ToolName, "tool_a", StringComparison.Ordinal));

        toolA.TotalCalls.ShouldBe(3);
        toolA.FailedCalls.ShouldBe(1);
        toolA.AverageDurationMs.ShouldNotBeNull();
        toolA.AverageDurationMs.Value.ShouldBe(200, tolerance: 1);
        toolA.ErrorRate.ShouldNotBeNull();
        toolA.ErrorRate.Value.ShouldBe(1.0 / 3, tolerance: 0.001);

        usage.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Ozet_cagri_sayisina_gore_sirali_ve_sinirli_doner()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordToolInvocationAsync(Invocation(runId, "az", TimeSpan.Zero));

        for (var index = 0; index < 3; index++)
        {
            await Store.RecordToolInvocationAsync(Invocation(runId, "cok", TimeSpan.Zero));
        }

        var usage = await Store.GetToolUsageAsync(new ToolUsageQuery { MaxTools = 1 });

        usage.Count.ShouldBe(1);
        string.Equals(usage[0].ToolName, "cok", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Ozet_baska_kiracinin_cagrilarini_saymaz()
    {
        var mine = AgentPrismId.NewId();
        var theirs = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(mine) with { TenantId = "kiraci-a" });
        await Store.StartRunAsync(TestData.Run(theirs) with { TenantId = "kiraci-b" });

        await Store.RecordToolInvocationAsync(Invocation(mine, "ortak", TimeSpan.Zero));
        await Store.RecordToolInvocationAsync(Invocation(theirs, "ortak", TimeSpan.Zero));

        var usage = await Store.GetToolUsageAsync(new ToolUsageQuery { TenantId = "kiraci-a" });

        usage.ShouldHaveSingleItem().TotalCalls.ShouldBe(1);
    }

    private static ToolInvocationRecord Invocation(Guid runId, string toolName, TimeSpan? duration)
        => new()
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = toolName,
            ToolCallId = Guid.NewGuid().ToString("N"),
            Duration = duration,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
