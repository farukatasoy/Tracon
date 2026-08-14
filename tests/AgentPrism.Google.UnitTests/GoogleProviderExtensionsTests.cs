using AgentPrism.Google.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// <c>UseGoogle()</c> kaydinin sekli, tekrarlanmasi ve yapilandirmadan baglanmasi.
/// </summary>
public sealed class GoogleProviderExtensionsTests
{
    [Fact]
    public void Tek_saglayici_kaydeder()
    {
        using var provider = Build(builder => builder.UseGoogle(TestData.ApiKey));

        provider.GetServices<IModelProvider>().Select(static p => p.Name)
            .ShouldBe([GoogleProviderNames.Google]);
    }

    [Fact]
    public void Saglayici_adi_gemini_degil_google()
    {
        // Ad kararlidir: agent tanimlari veritabaninda bu adla saklanir.
        GoogleProviderNames.Google.ShouldBe("google");
    }

    [Fact]
    public void Ikinci_cagri_saglayiciyi_cogaltmaz()
    {
        using var provider = Build(builder => builder
            .UseGoogle(TestData.ApiKey)
            .UseGoogle(options => options.DefaultModel = TestData.Model));

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);
        provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value.DefaultModel
            .ShouldBe(TestData.Model);
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(1);
    }

    [Fact]
    public void Tek_fabrika_paylasilir()
    {
        using var provider = Build(builder => builder.UseGoogle(TestData.ApiKey));

        provider.GetRequiredService<GoogleChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<GoogleChatClientFactory>());
    }

    [Fact]
    public void Yapilandirmadan_okur()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultModel"] = TestData.Model,
                ["Endpoint"] = "https://ornek.gecit",
                ["ApiVersion"] = "v1beta",
                ["Timeout"] = "00:00:30",
                ["Models:0:Name"] = TestData.Model,
                ["Models:0:ContextWindowTokens"] = "1048576",
                ["Models:0:SupportsReasoning"] = "true",
            })
            .Build();

        using var provider = Build(builder => builder.UseGoogle(configuration));

        var options = provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value;

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultModel.ShouldBe(TestData.Model);
        options.Endpoint.ShouldBe(new Uri("https://ornek.gecit"));
        options.ApiVersion.ShouldBe("v1beta");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Models.Single().ContextWindowTokens.ShouldBe(1048576);
        options.Models.Single().SupportsReasoning.ShouldBeTrue();
    }

    [Fact]
    public void Yapilandirmadan_gelen_goreli_adres_reddedilir()
    {
        // HATA-S3-003: Bind() eskiden Uri.TryCreate(..., UriKind.Absolute, ...)
        // basarisiz olunca Endpoint'i hic atamiyordu; deger doğrulayiciya
        // ulasmadan sessizce eleniyordu.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "sadece-bir-yol",
            })
            .Build();

        using var provider = Build(builder => builder.UseGoogle(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value)
            .Message.ShouldContain(nameof(GoogleProviderOptions.Endpoint));
    }

    [Fact]
    public void Yapilandirmadan_gelen_adsiz_model_reddedilir()
    {
        // HATA-S3-004: BindModels() eskiden bos Name'li ogeyi listeye hic
        // eklemiyordu; doğrulayici boş isimli bir öge asla görmüyordu.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Models:0:Name"] = TestData.Model,
                ["Models:1:Name"] = "",
            })
            .Build();

        using var provider = Build(builder => builder.UseGoogle(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value)
            .Message.ShouldContain(nameof(GoogleProviderOptions.Models));
    }

    [Fact]
    public void Anahtarsiz_kayit_baslangicta_hata_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseGoogle(options => options.DefaultModel = TestData.Model);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value)
            .Message.ShouldContain(nameof(GoogleProviderOptions.ApiKey));
    }

    [Fact]
    public void Bos_anahtar_argumani_reddedilir()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddAgentPrism().UseGoogle("   "));
    }

    [Fact]
    public void Katalog_yapilandirmadan_gelir_ve_saglayiciya_yansir()
    {
        using var provider = Build(builder => builder.UseGoogle(TestData.ApiKey, options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.6-flash" });
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.1-pro-preview" });
        }));

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List().Single();

        descriptor.Name.ShouldBe(GoogleProviderNames.Google);
        descriptor.Models.Select(static m => m.Name).ShouldBe(["gemini-3.1-pro-preview", "gemini-3.6-flash"]);
    }

    private static ServiceProvider Build(Action<IAgentPrismBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddAgentPrism());

        return services.BuildServiceProvider();
    }
}
