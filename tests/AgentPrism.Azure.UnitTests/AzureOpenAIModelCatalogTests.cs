using AgentPrism.Azure.UnitTests.Infrastructure;

namespace AgentPrism.Azure.UnitTests;

/// <summary>Katalogun yalnizca yapilandirmadan gelmesi (K-032).</summary>
/// <remarks>
/// Azure'da bu kural daha da baglayicidir: katalogdaki ad bir model adi degil,
/// o kaynakta tanimli bir deployment adidir ve iki tuketicinin listeleri birbirine
/// benzemek zorunda degildir.
/// </remarks>
public sealed class AzureOpenAIModelCatalogTests
{
    [Fact]
    public void Yerlesik_liste_yoktur()
    {
        // AgentPrism deployment listesi tasimaz; ayar bossa katalog da bostur.
        AzureOpenAIModelCatalog.Build(TestData.Options()).ShouldBeEmpty();
    }

    [Fact]
    public void Ada_gore_siralar()
    {
        var catalog = AzureOpenAIModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "uretim-gpt" });
            options.Models.Add(new ModelDescriptor { Name = "deneme-gpt" });
            options.Models.Add(new ModelDescriptor { Name = "muhasebe-gpt" });
        }));

        catalog.Select(static model => model.Name)
            .ShouldBe(["deneme-gpt", "muhasebe-gpt", "uretim-gpt"]);
    }

    [Fact]
    public void Ayni_ad_tekrarlanirsa_son_tanim_kazanir()
    {
        var catalog = AzureOpenAIModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment, DisplayName = "eski" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment, DisplayName = "yeni" });
        }));

        catalog.Single().DisplayName.ShouldBe("yeni");
    }

    [Fact]
    public void Adsiz_girdiler_yok_sayilir()
    {
        var catalog = AzureOpenAIModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "  " });
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment });
        }));

        catalog.Single().Name.ShouldBe(TestData.Deployment);
    }

    [Fact]
    public void Katalogda_olmayan_deployment_reddedilmez()
    {
        // Katalog bir dogrulama listesi degildir (K-032): Azure'da yeni bir
        // deployment acmak AgentPrism yapilandirmasinin guncellenmesini beklememelidir.
        var provider = new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Deployment }]);

        using var chatClient = provider.CreateChatClient(TestData.Binding("katalogda-olmayan-deployment"));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Null_ayar_reddedilir()
        => Should.Throw<ArgumentNullException>(() => AzureOpenAIModelCatalog.Build(null!));
}
