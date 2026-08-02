using AgentPrism.OpenAI.UnitTests.Infrastructure;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Model katalogunun yapilandirmadan nasil kuruldugunu dogrular.
/// </summary>
/// <remarks>
/// AgentPrism yerlesik bir model listesi tasimaz. Olculdu (2026-08-02): koda
/// gomulen liste, gercek bir hesabin erisebildigi modellerin hicbirini icermiyordu
/// ve listedeki bir modele yapilan cagri <c>HTTP 403 model_not_found</c> dondu.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-032.
/// </remarks>
public sealed class OpenAIModelCatalogTests
{
    [Fact]
    public void Yapilandirma_bos_ise_katalog_bostur()
        => OpenAIModelCatalog.Build(TestData.Options()).ShouldBeEmpty();

    [Fact]
    public void Yapilandirmadan_gelen_modeller_kataloga_girer()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "gpt-5.4-mini", ContextWindowTokens = 400_000 });
            o.Models.Add(new ModelDescriptor { Name = "gpt-5.6-terra" });
        });

        var catalog = OpenAIModelCatalog.Build(options);

        catalog.Count.ShouldBe(2);
        catalog.Single(static model => string.Equals(model.Name, "gpt-5.4-mini", StringComparison.Ordinal))
            .ContextWindowTokens.ShouldBe(400_000);
    }

    [Fact]
    public void Ayni_ad_iki_kez_verilirse_son_tanim_kazanir()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "gpt-5.4-mini", InputCostPerMillionTokens = 1m });
            o.Models.Add(new ModelDescriptor { Name = "GPT-5.4-MINI", InputCostPerMillionTokens = 2m });
        });

        var catalog = OpenAIModelCatalog.Build(options);

        catalog.ShouldHaveSingleItem().InputCostPerMillionTokens.ShouldBe(2m);
    }

    [Fact]
    public void Katalog_ada_gore_siralidir()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "zeta" });
            o.Models.Add(new ModelDescriptor { Name = "alfa" });
        });

        OpenAIModelCatalog.Build(options).Select(static model => model.Name).ShouldBe(["alfa", "zeta"]);
    }

    [Fact]
    public void Adsiz_girdi_yok_sayilir()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "   " });
            o.Models.Add(new ModelDescriptor { Name = "gecerli" });
        });

        OpenAIModelCatalog.Build(options).ShouldHaveSingleItem().Name.ShouldBe("gecerli");
    }

    [Fact]
    public void Null_ayar_reddedilir()
        => Should.Throw<ArgumentNullException>(() => OpenAIModelCatalog.Build(null!));
}
