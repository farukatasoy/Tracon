namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// Agac boyunca paylasilan butcenin sayaclari.
/// </summary>
public sealed class AgentRunBudgetTests
{
    [Fact]
    public void Sinirsiz_butce_her_zaman_yer_ayirir()
    {
        var budget = new AgentRunBudget();

        for (var index = 0; index < 100; index++)
        {
            budget.TryReserveRun().ShouldBeTrue();
        }

        budget.StartedRuns.ShouldBe(100);
    }

    [Fact]
    public void Sayi_siniri_asilinca_yer_ayrilmaz()
    {
        var budget = new AgentRunBudget { MaxTotalRuns = 2 };

        budget.TryReserveRun().ShouldBeTrue();
        budget.TryReserveRun().ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();

        // Basarisiz deneme sayaci artirmaz; aksi halde sinira ulasmis bir agacta
        // her yeni deneme arayuzde gercekte baslamamis calistirmalar gosterirdi.
        budget.StartedRuns.ShouldBe(2);
    }

    [Fact]
    public void Token_siniri_dolunca_yeni_calistirma_baslamaz()
    {
        var budget = new AgentRunBudget { MaxTotalTokens = 1_000 };

        budget.TryReserveRun().ShouldBeTrue();
        budget.RecordUsage(999);
        budget.TryReserveRun().ShouldBeTrue();

        budget.RecordUsage(1);

        budget.IsTokenBudgetExhausted.ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();
        budget.DescribeExhaustion().ShouldContain("token budget is exhausted", Case.Sensitive);
    }

    [Fact]
    public void Negatif_kullanim_yok_sayilir()
    {
        var budget = new AgentRunBudget();

        budget.RecordUsage(-5);

        budget.ConsumedTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Es_zamanli_yer_ayirma_siniri_asmaz()
    {
        // Alt calistirmalar es zamanli baslar: MAF'in arka plan agent gorevleri
        // bloke etmeden calisir ve ayni butce birden cok is parcaciginda okunur.
        var budget = new AgentRunBudget { MaxTotalRuns = 10 };
        var granted = 0;

        await Parallel.ForAsync(0, 200, (_, _) =>
        {
            if (budget.TryReserveRun())
            {
                Interlocked.Increment(ref granted);
            }

            return ValueTask.CompletedTask;
        });

        granted.ShouldBe(10);
        budget.StartedRuns.ShouldBe(10);
    }

    [Fact]
    public void Ayarlardan_uretilen_butce_varsayilanlari_tasir()
    {
        var budget = new AgentPrismAgentGraphOptions().CreateBudget();

        budget.MaxDepth.ShouldBe(3);
        budget.MaxTotalTokens.ShouldBe(200_000);
        budget.MaxTotalRuns.ShouldBe(25);
    }

    [Fact]
    public void Sifir_deger_sinirlamayi_kaldirir()
    {
        var budget = new AgentPrismAgentGraphOptions
        {
            MaxTotalTokens = 0,
            MaxTotalRuns = 0,
        }.CreateBudget();

        budget.MaxTotalTokens.ShouldBeNull();
        budget.MaxTotalRuns.ShouldBeNull();
    }
}
