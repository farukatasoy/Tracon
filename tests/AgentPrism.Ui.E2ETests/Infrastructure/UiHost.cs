using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// AgentPrism'i gercek bir Kestrel sunucusunda ayaga kaldirir.
/// </summary>
/// <remarks>
/// <para>
/// Faz 4'un <c>TestServer</c> deseni buraya tasinamaz: <c>TestServer</c> gercek
/// bir soket acmaz ve bir tarayici ona baglanamaz. Bu yuzden test kendi
/// <see cref="WebApplication"/> ornegini isletim sisteminin verdigi bir portta
/// baslatir.
/// </para>
/// <para>
/// Depolar bellek icidir ve model saglayicisi aga cikmaz; test hicbir dis
/// bagimlilik istemez.
/// </para>
/// </remarks>
internal sealed class UiHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ScriptedModelProvider _provider;

    private UiHost(WebApplication app, ScriptedModelProvider provider, string baseAddress, string prefix)
    {
        _app = app;
        _provider = provider;
        BaseAddress = baseAddress;
        Prefix = prefix;
    }

    /// <summary>Sunucunun kok adresi. Ornek: <c>http://127.0.0.1:53412</c>.</summary>
    public string BaseAddress { get; }

    /// <summary>AgentPrism'in baglandigi yol oneki.</summary>
    public string Prefix { get; }

    /// <summary>Arayuzun adresi.</summary>
    public string UiAddress => BaseAddress + Prefix;

    /// <summary>Bir sunucu baslatir.</summary>
    /// <param name="prefix">Yol oneki.</param>
    /// <param name="authToken">Bearer token. Verilirse token katmani acilir.</param>
    /// <param name="configureServices">
    /// Ek servis kaydi (ornek: rol policy'lerini test etmek icin authentication/authorization).
    /// </param>
    /// <returns>Calisan sunucu.</returns>
    public static async Task<UiHost> StartAsync(
        string prefix = "/agentprism",
        string? authToken = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        configureServices?.Invoke(builder.Services);

        var provider = new ScriptedModelProvider();

        builder.Services.AddAgentPrism()
            .AddModelProvider(provider)
            .AddToolsFrom(typeof(OrderTools))
            .UseUI()
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                DisplayName = "Support assistant",
                Description = "Testlerde kullanilan kod agent'i.",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModelProvider.ProviderName,
                    Model = ScriptedModelProvider.ModelName,
                },
                ToolNames = ["get_order_status"],
                Origin = AgentDefinitionOrigin.Code,
            })
            .AddAgent(new AgentDefinition
            {
                Name = "yonlendirici",
                DisplayName = "Router",
                Description = "Isi support agent'ina devreden kod agent'i.",
                Instructions = "Gerekirse support agent'ini cagir.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModelProvider.ProviderName,
                    Model = ScriptedModelProvider.ModelName,
                },
                CallableAgentNames = ["support"],
                Origin = AgentDefinitionOrigin.Code,
            });

        var app = builder.Build();

        // Port 0: isletim sistemi bos bir port secer. Sabit bir port, paralel
        // kosan testlerde carpisir.
        app.Urls.Add("http://127.0.0.1:0");

        app.MapAgentPrism(prefix, options =>
        {
            if (authToken is { Length: > 0 })
            {
                options.AuthToken = authToken;
            }
        });

        await app.StartAsync();

        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel bir adres bildirmedi.");

        return new UiHost(app, provider, address.TrimEnd('/'), '/' + prefix.Trim('/'));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();

        _provider.Dispose();
    }
}
