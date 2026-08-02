namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// Kaydetme anindaki statik dongu denetimi.
/// </summary>
/// <remarks>
/// Denetim calisma anina birakilamaz: dongulu bir grafik ancak derinlik sayaci
/// dolduktan sonra fark edilir ve o noktada token zaten harcanmistir.
/// </remarks>
public sealed class AgentCallGraphTests
{
    [Fact]
    public void Bos_liste_gecerlidir()
        => AgentCallGraph.Validate("a", [], Descriptors()).ShouldBeNull();

    [Fact]
    public void Kendini_cagirma_reddedilir()
    {
        var problem = AgentCallGraph.Validate("a", ["a"], Descriptors(("a", [])));

        problem.ShouldNotBeNull();
        problem.ShouldContain("kendisini cagiramaz", Case.Sensitive);
    }

    [Fact]
    public void Bilinmeyen_ad_reddedilir()
    {
        var problem = AgentCallGraph.Validate("a", ["yok"], Descriptors(("a", [])));

        problem.ShouldNotBeNull();
        problem.ShouldContain("katalogda yok", Case.Sensitive);
    }

    [Fact]
    public void Dolayli_dongu_reddedilir()
    {
        // b -> c -> a zinciri katalogda hazir; a -> b eklenirse dongu kapanir.
        var descriptors = Descriptors(("a", []), ("b", ["c"]), ("c", ["a"]));

        var problem = AgentCallGraph.Validate("a", ["b"], descriptors);

        problem.ShouldNotBeNull();
        problem.ShouldContain("dongu", Case.Sensitive);
        problem.ShouldContain("a -> b -> c -> a", Case.Sensitive);
    }

    [Fact]
    public void Dongusuz_derin_zincir_kabul_edilir()
    {
        var descriptors = Descriptors(("a", []), ("b", ["c"]), ("c", ["d"]), ("d", []));

        AgentCallGraph.Validate("a", ["b"], descriptors).ShouldBeNull();
    }

    [Fact]
    public void Elmas_bicimli_grafik_dongu_sayilmaz()
    {
        // a -> b, a -> c, ikisi de d'yi cagirir. Ayni dugume iki yoldan ulasmak
        // bir dongu DEGILDIR; ziyaret edilmis dugumu dongu sanan bir algoritma
        // bu gecerli grafigi reddederdi.
        var descriptors = Descriptors(("a", []), ("b", ["d"]), ("c", ["d"]), ("d", []));

        AgentCallGraph.Validate("a", ["b", "c"], descriptors).ShouldBeNull();
    }

    [Fact]
    public void Katalogdaki_eski_hal_degil_yeni_liste_denetlenir()
    {
        // Katalogda "a" henuz hicbir agent'i cagirmiyor. Denetim katalogdaki
        // eski hali kullansaydi yeni eklenen kenar hic gorulmez ve dongu kacardi.
        var descriptors = Descriptors(("a", []), ("b", ["a"]));

        AgentCallGraph.Validate("a", ["b"], descriptors).ShouldNotBeNull();
    }

    [Fact]
    public void Cok_uzun_zincir_yigin_tasmasi_uretmez()
    {
        // Cagri grafigi kullanici verisidir; ozyinelemeli bir gezinti yeterince
        // uzun bir zincirde sureci oldururdu.
        var chain = new List<AgentDescriptor>();

        for (var index = 0; index < 20_000; index++)
        {
            chain.Add(new AgentDescriptor
            {
                Name = $"n{index}",
                Origin = AgentDefinitionOrigin.Database,
                SourceName = "database",
                CallableAgentNames = index + 1 < 20_000 ? [$"n{index + 1}"] : [],
            });
        }

        AgentCallGraph.Validate("kok", ["n0"], chain).ShouldBeNull();
    }

    private static AgentDescriptor[] Descriptors(params (string Name, string[] Calls)[] entries)
        => [.. entries.Select(static entry => new AgentDescriptor
        {
            Name = entry.Name,
            Origin = AgentDefinitionOrigin.Database,
            SourceName = "database",
            CallableAgentNames = entry.Calls,
        })];
}
