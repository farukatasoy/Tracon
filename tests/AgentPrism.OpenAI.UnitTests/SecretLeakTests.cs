using System.Text.Json;
using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// API anahtarinin AgentPrism'in disariya verdigi hicbir ciktida gorunmedigini dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Bu testlerin korudugu sinir sudur: anahtar yalnizca OpenAI istemcisinin kimlik
/// dogrulama basligina gider. Katalog, saglayici defteri, ayar nesnesi, dogrulama
/// mesajlari, istisna mesajlari, istemci ustverisi ve gunluk satirlari anahtari
/// <strong>hicbir kosulda</strong> tasiyamaz.
/// </para>
/// <para>
/// Npgsql baglanti dizesindeki parolayi kendisi maskeler; OpenAI istemcisinde ayni
/// garanti yoktur. Bu yuzden sinir burada testle korunur.
/// </para>
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Secret = "cok-gizli-anahtar-DENEME-9f3a2b";

    [Fact]
    public void Saglayici_defteri_ciktisinda_anahtar_yok()
    {
        using var provider = BuildProvider();

        var json = JsonSerializer.Serialize(
            provider.GetRequiredService<IModelProviderRegistry>().List(),
            JsonOptions);

        json.ShouldNotContain(Secret);
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
        // Ayar nesnesi record OLMAMALIDIR: derleyicinin urettigi ToString tum
        // ozellikleri yazar ve anahtari ilk gunluk satirinda ifsa ederdi.
        // Varsayilan object.ToString yalnizca tip adini yazar.
        typeof(OpenAIProviderOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Dogrulama_mesajlari_anahtar_icermez()
    {
        var validator = new OpenAIProviderOptionsValidator();

        var result = validator.Validate(
            name: null,
            new OpenAIProviderOptions
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
        var factory = new OpenAIChatClientFactory(new OpenAIProviderOptions { ApiKey = Secret });

        var exception = Should.Throw<AgentPrismException>(() => factory.CreateChatClient(
            new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "  " },
            OpenAIApiSurface.ChatCompletions));

        exception.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public void Istemci_ustverisi_anahtar_icermez()
    {
        // Faz 6'daki telemetri bu ustveriyi span etiketlerine yazar.
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
        services.AddAgentPrism().UseOpenAI(Secret, options => options.DefaultModel = "gpt-4o-mini");

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
        services.AddAgentPrism().UseOpenAI(Secret);

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IOptions<OpenAIProviderOptions>>().Value.ApiKey.ShouldBe(Secret);
        loggerProvider.AllText.ShouldNotContain(Secret);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    private static string Text(object value) => value.ToString() ?? string.Empty;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAI(Secret);

        return services.BuildServiceProvider();
    }
}
