using AgentPrism.Testing.Internal;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing;

/// <summary>
/// Aga cikmayan, yapilandirilabilir sahte model saglayicisi.
/// </summary>
/// <remarks>
/// <para>
/// Beş fluent metot (<see cref="RespondsWith(string[])"/>, <see cref="EchoesUserMessage"/>,
/// <see cref="CallsTool"/>, <see cref="ForModel"/>, <see cref="WithModel"/>)
/// AgentPrism'in test paketlerinde bugun bes ayri dosyaya kopyalanmis sahte
/// saglayici davranisinin tumunu kapsar. Gerekce: docs/39-TEST-PAKETI.md.
/// </para>
/// <para>
/// Her modelin kendi <strong>sirali yanit kuyrugu</strong> vardir
/// (<see cref="ForModel"/> ile secilir; secilmezse varsayilan kuyruk kullanilir).
/// Bir cagri sıradaki adimi coker; kuyruk tukendiginde her sonraki cagri
/// <see cref="EchoesUserMessage"/> ile ayarlanan yankiyi ya da sabit bir
/// yanit dondurur. Bu davranis saglayicinin omru boyunca kalicidir — mesaj
/// gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var provider = new FakeModelProvider()
///     .CallsTool("get_order_status", new { orderId = "ORD-7" })
///     .EchoesUserMessage();
/// </code>
/// </example>
public sealed class FakeModelProvider : IModelProvider, IDisposable
{
    private readonly Dictionary<string, FakeModelScript> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModelDescriptor> _models = [];
    private readonly List<FakeModelRequest> _requests = [];
    private readonly Lock _gate = new();
    private readonly FakeModelScript _default = new();

    private FakeModelScript _current;

    /// <summary>Yeni bir sahte saglayici olusturur.</summary>
    /// <param name="name">Saglayici adi.</param>
    public FakeModelProvider(string name = "fake")
    {
        Name = name;
        _current = _default;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models => _models.Count > 0
        ? _models
        : [new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 }];

    /// <summary>Saglayiciya ulasan istekler; en yeni sonuncudur.</summary>
    public IReadOnlyList<FakeModelRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Bu saglayiciya bir model tanimlar.</summary>
    /// <param name="descriptor">Model tanimi.</param>
    /// <returns>Zincirin devami.</returns>
    public FakeModelProvider WithModel(ModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _models.Add(descriptor);

        return this;
    }

    /// <summary>
    /// Sirayla dondurulecek yanitlari sıraya ekler. Kuyruk tukendiginde
    /// <see cref="EchoesUserMessage"/> ayarlanmadiysa sabit bir yanit dondurulur.
    /// </summary>
    /// <param name="responses">Sirayla dondurulecek metinler.</param>
    /// <returns>Zincirin devami.</returns>
    public FakeModelProvider RespondsWith(params string[] responses)
    {
        ArgumentNullException.ThrowIfNull(responses);

        foreach (var response in responses)
        {
            _current.Enqueue(new FakeStep { Text = response });
        }

        return this;
    }

    /// <summary>
    /// Sıraya, belirli bir token kullanimi bildiren tek bir metin yaniti ekler.
    /// </summary>
    /// <param name="response">Dondurulecek metin.</param>
    /// <param name="inputTokens">Bildirilecek girdi token sayisi.</param>
    /// <param name="outputTokens">Bildirilecek cikti token sayisi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <remarks>Maliyet/kullanim metriklerini uctan uca test eden senaryolar icindir.</remarks>
    public FakeModelProvider RespondsWith(string response, int inputTokens, int outputTokens)
    {
        ArgumentNullException.ThrowIfNull(response);

        _current.Enqueue(new FakeStep
        {
            Text = response,
            Usage = new FakeUsage(inputTokens, outputTokens),
        });

        return this;
    }

    /// <summary>
    /// Kuyruk tukendiginde gelen son kullanici mesajini yankilamaya baslar.
    /// </summary>
    /// <returns>Zincirin devami.</returns>
    public FakeModelProvider EchoesUserMessage()
    {
        _current.Fallback = FakeFallbackKind.EchoUserMessage;

        return this;
    }

    /// <summary>
    /// Kuyruk tukendiginde, gecmisteki SON tool sonucunu yankilamaya baslar.
    /// </summary>
    /// <param name="prefix">Sonucun basina eklenecek metin.</param>
    /// <param name="inputTokens">Verilirse, her yanitla birlikte bildirilecek girdi token sayisi.</param>
    /// <param name="outputTokens">Verilirse, her yanitla birlikte bildirilecek cikti token sayisi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <remarks>
    /// Bir tool zincirinin (<see cref="CallsTool"/> ile kurulan) son adiminda,
    /// nihai yanitin GERCEKTEN tool'un dondurdugu sonuca bagli olmasi gereken
    /// senaryolar icindir — ornek: bir alt agent'a devir sonucu.
    /// </remarks>
    public FakeModelProvider EchoesLastToolResult(string prefix = "", int? inputTokens = null, int? outputTokens = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        _current.Fallback = FakeFallbackKind.EchoLastToolResult;
        _current.FallbackPrefix = prefix;
        _current.FallbackUsage = inputTokens is { } input && outputTokens is { } output
            ? new FakeUsage(input, output)
            : null;

        return this;
    }

    /// <summary>Sıraya bir tool cagrisi ekler.</summary>
    /// <param name="toolName">Cagrilacak tool'un adi.</param>
    /// <param name="arguments">
    /// Cagri argumanlari. <see cref="IDictionary{TKey, TValue}"/> degilse ozellikleri
    /// yansima ile okunur (anonim tip icin uygundur).
    /// </param>
    /// <returns>Zincirin devami.</returns>
    public FakeModelProvider CallsTool(string toolName, object? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        _current.Enqueue(new FakeStep { ToolName = toolName, ToolArguments = arguments });

        return this;
    }

    /// <summary>
    /// Belirli bir model adi icin ayri bir yanit kuyrugu tanimlar. <paramref name="configure"/>
    /// govdesindeki <see cref="RespondsWith(string[])"/>/<see cref="EchoesUserMessage"/>/<see cref="CallsTool"/>
    /// cagrilari yalniz bu modelin kuyrugunu etkiler.
    /// </summary>
    /// <param name="modelId">Model adi.</param>
    /// <param name="configure">Bu modelin kuyrugunu dolduran yapilandirma.</param>
    /// <returns>Zincirin devami.</returns>
    public FakeModelProvider ForModel(string modelId, Action<FakeModelProvider> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_scripts.TryGetValue(modelId, out var script))
        {
            script = new FakeModelScript();
            _scripts[modelId] = script;

            if (!_models.Exists(model => string.Equals(model.Name, modelId, StringComparison.OrdinalIgnoreCase)))
            {
                _models.Add(new ModelDescriptor { Name = modelId });
            }
        }

        var previous = _current;
        _current = script;

        try
        {
            configure(this);
        }
        finally
        {
            _current = previous;
        }

        return this;
    }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var script = _scripts.TryGetValue(binding.Model, out var forModel) ? forModel : _default;

        var client = new FakeChatClient(script, Record);

        return client.AsBuilder().UseFunctionInvocation().Build();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Sahte saglayicinin serbest birakilacak kaynagi yok.
    }

    private void Record(FakeModelRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }
    }
}
