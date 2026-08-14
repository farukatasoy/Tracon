using AgentPrism.Anthropic.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Anthropic.UnitTests;

/// <summary>
/// <c>UseAnthropic()</c> kaydinin sekli, tekrarlanmasi ve yapilandirmadan
/// baglanmasi.
/// </summary>
public sealed class AnthropicProviderExtensionsTests
{
    [Fact]
    public void Tek_saglayici_kaydeder()
    {
        using var provider = Build(builder => builder.UseAnthropic(TestData.ApiKey));

        var names = provider.GetServices<IModelProvider>().Select(static p => p.Name).ToList();

        names.ShouldBe([AnthropicProviderNames.Anthropic]);
    }

    [Fact]
    public void Ikinci_cagri_saglayiciyi_cogaltmaz()
    {
        using var provider = Build(builder => builder
            .UseAnthropic(TestData.ApiKey)
            .UseAnthropic(options => options.DefaultModel = TestData.Model));

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);

        // Ikinci cagri ayarlari birlestirir; defter "ayni ad iki kez" hatasi vermez.
        provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value.DefaultModel
            .ShouldBe(TestData.Model);
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(1);
    }

    [Fact]
    public void Tek_fabrika_paylasilir()
    {
        using var provider = Build(builder => builder.UseAnthropic(TestData.ApiKey));

        // Tek istemci, tek HTTP baglanti havuzu.
        provider.GetRequiredService<AnthropicChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<AnthropicChatClientFactory>());
    }

    [Fact]
    public void Yapilandirmadan_okur()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultModel"] = TestData.Model,
                ["Endpoint"] = "https://ornek.gecit/v1",
                ["DefaultMaxOutputTokens"] = "8192",
                ["MaxRetries"] = "0",
                ["Timeout"] = "00:00:30",
                ["Models:0:Name"] = TestData.Model,
                ["Models:0:ContextWindowTokens"] = "200000",
                ["Models:0:InputCostPerMillionTokens"] = "3",
            })
            .Build();

        using var provider = Build(builder => builder.UseAnthropic(configuration));

        var options = provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultModel.ShouldBe(TestData.Model);
        options.Endpoint.ShouldBe(new Uri("https://ornek.gecit/v1"));
        options.DefaultMaxOutputTokens.ShouldBe(8192);
        options.MaxRetries.ShouldBe(0);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Models.Single().ContextWindowTokens.ShouldBe(200000);
        options.Models.Single().InputCostPerMillionTokens.ShouldBe(3);
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

        using var provider = Build(builder => builder.UseAnthropic(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value)
            .Message.ShouldContain(nameof(AnthropicProviderOptions.Endpoint));
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

        using var provider = Build(builder => builder.UseAnthropic(configuration));

        Should.Throw<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value)
            .Message.ShouldContain(nameof(AnthropicProviderOptions.Models));
    }

    [Fact]
    public void Anahtarsiz_kayit_baslangicta_hata_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseAnthropic(options => options.DefaultModel = TestData.Model);

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(AnthropicProviderOptions.ApiKey));
    }

    [Fact]
    public void Bos_anahtar_argumani_reddedilir()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddAgentPrism().UseAnthropic("   "));
    }

    [Fact]
    public void Katalog_yapilandirmadan_gelir_ve_saglayiciya_yansir()
    {
        using var provider = Build(builder => builder.UseAnthropic(TestData.ApiKey, options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "claude-opus-5" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        }));

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List().Single();

        descriptor.Name.ShouldBe(AnthropicProviderNames.Anthropic);
        descriptor.Models.Select(static m => m.Name).ShouldBe(["claude-opus-5", TestData.Model]);
    }

    private static ServiceProvider Build(Action<IAgentPrismBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddAgentPrism());

        return services.BuildServiceProvider();
    }
}
