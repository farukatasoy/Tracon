using AgentPrism.Anthropic.UnitTests.Infrastructure;

namespace AgentPrism.Anthropic.UnitTests;

/// <summary>Katalogun yalnizca yapilandirmadan gelmesi (K-032).</summary>
public sealed class AnthropicModelCatalogTests
{
    [Fact]
    public void Yerlesik_liste_yoktur()
    {
        // AgentPrism model listesi tasimaz; ayar bossa katalog da bostur.
        AnthropicModelCatalog.Build(TestData.Options()).ShouldBeEmpty();
    }

    [Fact]
    public void Ada_gore_siralar()
    {
        var catalog = AnthropicModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "claude-sonnet-5" });
            options.Models.Add(new ModelDescriptor { Name = "claude-haiku-4-5-20251001" });
            options.Models.Add(new ModelDescriptor { Name = "claude-opus-5" });
        }));

        catalog.Select(static model => model.Name)
            .ShouldBe(["claude-haiku-4-5-20251001", "claude-opus-5", "claude-sonnet-5"]);
    }

    [Fact]
    public void Ayni_ad_tekrarlanirsa_son_tanim_kazanir()
    {
        var catalog = AnthropicModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "eski" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "yeni" });
        }));

        catalog.Single().DisplayName.ShouldBe("yeni");
    }

    [Fact]
    public void Adsiz_girdiler_yok_sayilir()
    {
        var catalog = AnthropicModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "  " });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        }));

        catalog.Single().Name.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Null_ayar_reddedilir()
        => Should.Throw<ArgumentNullException>(() => AnthropicModelCatalog.Build(null!));
}
