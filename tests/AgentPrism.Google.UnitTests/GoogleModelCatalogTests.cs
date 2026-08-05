using AgentPrism.Google.UnitTests.Infrastructure;

namespace AgentPrism.Google.UnitTests;

/// <summary>Katalogun yalnizca yapilandirmadan gelmesi (K-032).</summary>
public sealed class GoogleModelCatalogTests
{
    [Fact]
    public void Yerlesik_liste_yoktur()
    {
        // Olculdu (2026-08-05): gemini-2.5-flash "no longer available to new users"
        // dondu. Koda gomulu bir liste yayinlandigi gun bile yanlis olabilir.
        GoogleModelCatalog.Build(TestData.Options()).ShouldBeEmpty();
    }

    [Fact]
    public void Ada_gore_siralar()
    {
        var catalog = GoogleModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.6-flash" });
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.1-flash-lite" });
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.1-pro-preview" });
        }));

        catalog.Select(static model => model.Name)
            .ShouldBe(["gemini-3.1-flash-lite", "gemini-3.1-pro-preview", "gemini-3.6-flash"]);
    }

    [Fact]
    public void Ayni_ad_tekrarlanirsa_son_tanim_kazanir()
    {
        var catalog = GoogleModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "eski" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "yeni" });
        }));

        catalog.Single().DisplayName.ShouldBe("yeni");
    }

    [Fact]
    public void Adsiz_girdiler_yok_sayilir()
    {
        var catalog = GoogleModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "  " });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        }));

        catalog.Single().Name.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Null_ayar_reddedilir()
        => Should.Throw<ArgumentNullException>(() => GoogleModelCatalog.Build(null!));
}
