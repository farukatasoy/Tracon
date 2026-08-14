using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// <c>UseOpenAI()</c> cagrisinin servis kayitlarini dogrular.
/// </summary>
/// <remarks>
/// <c>UsePostgreSql()</c> mevcut depolarin yerini aldigi icin <c>Replace</c> kullanir;
/// bu cagri yeni saglayici <em>ekler</em>, bu yuzden <c>AddModelProvider</c> yeterlidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-025.
/// </remarks>
public sealed class OpenAIProviderExtensionsTests
{
    [Fact]
    public void UseOpenAI_iki_saglayici_kaydeder()
    {
        using var provider = BuildProvider();

        var names = provider.GetServices<IModelProvider>().Select(static p => p.Name).ToList();

        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.ChatCompletions, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.Responses, StringComparison.Ordinal));
    }

    [Fact]
    public void Saglayicilar_defterden_ada_gore_cozulur()
    {
        using var provider = BuildProvider();
        var registry = provider.GetRequiredService<IModelProviderRegistry>();

        using var chat = registry.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-4o-mini" });
        using var responses = registry.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.Responses, Model = "gpt-4o-mini" });

        chat.ShouldNotBeNull();
        responses.ShouldNotBeNull();
    }

    [Fact]
    public void Saglayici_adi_buyuk_kucuk_harfe_duyarsizdir()
    {
        using var provider = BuildProvider();

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "OpenAI", Model = "gpt-4o-mini" });

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Iki_saglayici_ayni_istemci_fabrikasini_paylasir()
    {
        // Tek OpenAIClient, tek HTTP baglanti havuzu. Iki fabrika kurulursa havuz parcalanir.
        using var provider = BuildProvider();

        var providers = provider.GetServices<IModelProvider>().OfType<OpenAIModelProvider>().ToList();

        providers.Count.ShouldBe(2);
        provider.GetRequiredService<OpenAIChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<OpenAIChatClientFactory>());
    }

    [Fact]
    public void Ikinci_cagri_saglayicilari_cogaltmaz()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .UseOpenAI(TestData.ApiKey)
            .UseOpenAI(options => options.DefaultModel = "gpt-4o");

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IModelProvider>().Count().ShouldBe(2);
        provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value.DefaultModel.ShouldBe("gpt-4o");
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(2);
    }

    [Fact]
    public void Katalog_varsayilan_olarak_bostur()
    {
        // AgentPrism yerlesik model listesi tasimaz; karar K-032.
        using var provider = BuildProvider();

        var descriptors = provider.GetRequiredService<IModelProviderRegistry>().List();

        descriptors.Count.ShouldBe(2);
        descriptors.ShouldAllBe(static descriptor => descriptor.Models.Count == 0);
    }

    [Fact]
    public void Yapilandirmadan_verilen_katalog_defterde_gorunur()
    {
        using var provider = BuildProvider(options =>
            options.Models.Add(new ModelDescriptor { Name = "gpt-5.4-mini" }));

        var descriptors = provider.GetRequiredService<IModelProviderRegistry>().List();

        descriptors.ShouldAllBe(static descriptor => descriptor.Models.Count == 1);
    }

    [Fact]
    public void Ayarlar_yapilandirmadan_okunur()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultModel"] = "gpt-4.1-mini",
                ["Endpoint"] = "https://ara-sunucu.example.com/v1/",
                ["Organization"] = "org-test",
                ["Timeout"] = "00:01:30",
                ["Models:0:Name"] = "ozel-model",
                ["Models:0:ContextWindowTokens"] = "65536",
                ["Models:0:InputCostPerMillionTokens"] = "1.25",
                ["Models:0:SupportsReasoning"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value;

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultModel.ShouldBe("gpt-4.1-mini");
        options.Endpoint.ShouldBe(new Uri("https://ara-sunucu.example.com/v1/"));
        options.Organization.ShouldBe("org-test");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(90));

        var model = options.Models.ShouldHaveSingleItem();
        model.Name.ShouldBe("ozel-model");
        model.ContextWindowTokens.ShouldBe(65_536);
        model.InputCostPerMillionTokens.ShouldBe(1.25m);
        model.SupportsReasoning.ShouldBeTrue();
        model.SupportsTools.ShouldBeTrue();
    }

    [Fact]
    public void Yapilandirmadan_gelen_model_katalogda_gorunur()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = "yalnizca-bu",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List()[0];

        descriptor.Models.ShouldHaveSingleItem().Name.ShouldBe("yalnizca-bu");
    }

    [Fact]
    public void Api_anahtari_bos_ise_dogrulama_hata_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(options => options.ApiKey = "   ");

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.ApiKey));
    }

    [Fact]
    public void Goreli_adres_reddedilir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(TestData.ApiKey, options => options.Endpoint = new Uri("/v1", UriKind.Relative));

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value);
    }

    [Fact]
    public void Yapilandirmadan_gelen_goreli_adres_reddedilir()
    {
        // HATA-S3-001: Bind() eskiden Uri.TryCreate(..., UriKind.Absolute, ...)
        // basarisiz olunca Endpoint'i hic atamiyordu; deger doğrulayiciya
        // ulasmadan sessizce eleniyordu.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "sadece-bir-yol",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value)
            .Message.ShouldContain(nameof(OpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Yapilandirmadan_gelen_adsiz_model_reddedilir()
    {
        // HATA-S3-002: BindModels() eskiden bos Name'li ogeyi listeye hic
        // eklemiyordu; doğrulayici boş isimli bir öge asla görmüyordu.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = "gpt-4o-mini",
                ["Models:1:Name"] = "",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(configuration);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value)
            .Message.ShouldContain(nameof(OpenAIProviderOptions.Models));
    }

    [Fact]
    public void Sifir_sure_siniri_reddedilir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(TestData.ApiKey, options => options.Timeout = TimeSpan.Zero);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value);
    }

    [Fact]
    public void Bos_api_anahtari_argumani_reddedilir()
    {
        var builder = new ServiceCollection().AddAgentPrism();

        Should.Throw<ArgumentException>(() => builder.UseOpenAI("  "));
    }

    [Fact]
    public void Null_zincir_reddedilir()
        => Should.Throw<ArgumentNullException>(
            () => OpenAIProviderExtensions.UseOpenAI(null!, TestData.ApiKey));

    private static ServiceProvider BuildProvider(Action<OpenAIProviderOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddAgentPrism().UseOpenAI(TestData.ApiKey, configure);

        return services.BuildServiceProvider();
    }
}
