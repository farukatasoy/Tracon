using System.Text.Json;
using AgentPrism.Google.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism.Google.UnitTests;

/// <summary>
/// API anahtarinin AgentPrism'in disariya verdigi hicbir ciktida gorunmedigini dogrular.
/// </summary>
/// <remarks>
/// Korunan sinir: anahtar yalnizca Google istemcisinin <c>x-goog-api-key</c> basligina
/// gider. Katalog, saglayici defteri, ayar nesnesi, dogrulama mesajlari, istisna
/// mesajlari, istemci ustverisi ve gunluk satirlari anahtari <strong>hicbir
/// kosulda</strong> tasiyamaz.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Secret = "cok-gizli-anahtar-DENEME-9f3a2b";

    [Fact]
    public void Saglayici_defteri_ciktisinda_anahtar_yok()
    {
        using var provider = BuildProvider();

        JsonSerializer.Serialize(
            provider.GetRequiredService<IModelProviderRegistry>().List(),
            JsonOptions).ShouldNotContain(Secret);
    }

    [Fact]
    public void Model_katalogu_ciktisinda_anahtar_yok()
    {
        using var provider = BuildProvider();

        foreach (var modelProvider in provider.GetServices<IModelProvider>())
        {
            JsonSerializer.Serialize(modelProvider.Models, JsonOptions).ShouldNotContain(Secret);
            Text(modelProvider).ShouldNotContain(Secret);
        }
    }

    [Fact]
    public void Ayar_nesnesi_kendi_ToString_metodunu_tanimlamaz()
    {
        // Ayar nesnesi record OLMAMALIDIR (K-035): derleyicinin urettigi ToString
        // tum ozellikleri yazar ve anahtari ilk gunluk satirinda ifsa ederdi.
        typeof(GoogleProviderOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Dogrulama_mesajlari_anahtar_icermez()
    {
        var result = new GoogleProviderOptionsValidator().Validate(
            name: null,
            new GoogleProviderOptions
            {
                ApiKey = Secret,
                Endpoint = new Uri("/v1", UriKind.Relative),
                Timeout = TimeSpan.Zero,
            });

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures!).ShouldNotContain(Secret);
    }

    [Fact]
    public void Derleme_hatasi_mesaji_anahtar_icermez()
    {
        using var factory = new GoogleChatClientFactory(new GoogleProviderOptions { ApiKey = Secret });

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = GoogleProviderNames.Google, Model = "  " }));

        exception.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public void Istemci_ustverisi_anahtar_icermez()
    {
        using var provider = BuildProvider();

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        (metadata.ProviderUri?.ToString() ?? string.Empty).ShouldNotContain(Secret);
        (metadata.DefaultModelId ?? string.Empty).ShouldNotContain(Secret);
        Text(chatClient).ShouldNotContain(Secret);
    }

    [Fact]
    public void Saglayici_kurulumu_ve_istemci_uretimi_anahtari_gunluge_yazmaz()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddAgentPrism().UseGoogle(Secret, options =>
        {
            options.DefaultModel = TestData.Model;
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        });

        using var serviceProvider = services.BuildServiceProvider();

        using var chatClient = serviceProvider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding("katalogda-olmayan-model"));

        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    [Fact]
    public void Ayarlar_dogrulandiginda_anahtar_gunluge_yazilmaz()
    {
        using var loggerProvider = new RecordingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));
        services.AddAgentPrism().UseGoogle(Secret);

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IOptions<GoogleProviderOptions>>().Value.ApiKey.ShouldBe(Secret);
        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string Text(object value) => value.ToString() ?? string.Empty;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseGoogle(Secret);

        return services.BuildServiceProvider();
    }
}
