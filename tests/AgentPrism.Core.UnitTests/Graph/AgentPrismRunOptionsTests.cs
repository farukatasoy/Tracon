namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// <see cref="AgentPrismRunOptions.Clone"/> davranisi.
/// </summary>
/// <remarks>
/// K-044'te ogrenildi: ayarlari kopyalayan bir ara katman kimligi dusurdugunde
/// istemciye bildirilen calistirma kimligi hicbir kayda karsilik gelmiyordu.
/// Agac alanlari icin ayni tuzak daha sinsidir - dusen bir <c>Depth</c> degeri
/// ozyineleme korumasini sessizce devre disi birakir.
/// </remarks>
public sealed class AgentPrismRunOptionsTests
{
    [Fact]
    public void Clone_agac_alanlarinin_hepsini_korur()
    {
        var budget = new AgentRunBudget { MaxDepth = 2, MaxTotalTokens = 500 };

        var original = new AgentPrismRunOptions
        {
            RunId = AgentPrismId.NewId(),
            ParentRunId = AgentPrismId.NewId(),
            RootRunId = AgentPrismId.NewId(),
            Depth = 2,
            Budget = budget,
        };

        var clone = original.Clone().ShouldBeOfType<AgentPrismRunOptions>();

        clone.RunId.ShouldBe(original.RunId);
        clone.ParentRunId.ShouldBe(original.ParentRunId);
        clone.RootRunId.ShouldBe(original.RootRunId);
        clone.Depth.ShouldBe(2);

        // Butce AYNI ornek olmalidir. Deger esitligi yetmez: kopya bir butce her
        // dala kendi sinirini verirdi.
        clone.Budget.ShouldBeSameAs(budget);
    }

    [Fact]
    public void Clone_bos_ayarlari_bos_birakir()
    {
        var clone = new AgentPrismRunOptions().Clone().ShouldBeOfType<AgentPrismRunOptions>();

        clone.RunId.ShouldBeNull();
        clone.ParentRunId.ShouldBeNull();
        clone.RootRunId.ShouldBeNull();
        clone.Depth.ShouldBe(0);
        clone.Budget.ShouldBeNull();
    }
}
