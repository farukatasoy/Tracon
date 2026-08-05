using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Bir agent'in cagirabilecegi alt agent'i sarar; derinlik, butce ve kiraci
/// sinirlarini uygular ve alt calistirmayi agaca baglar.
/// </summary>
/// <remarks>
/// <para>
/// Bu bir <see cref="IAgentDecorator"/> <strong>degildir</strong>. Dekoratorler
/// katalogdan cozulen <em>her</em> agent'a uygulanir; bu sarmalayici ise yalnizca
/// bir agent baska bir agent'in gozunde gorundugunde araya girer. Dekorator
/// sirasina (kayit 0 → telemetri 10 → onay 20) dokunulmaz.
/// </para>
/// <para>
/// <strong>Alt agent gec cozulur.</strong> Boylece cagiran agent'in derlenmis
/// kopyasi, alt agent'in tanimi degistiginde bayatlamaz.
/// </para>
/// <para>
/// 🚨 Microsoft Agent Framework alt agent'i <c>options = null</c> ile cagirir
/// (Faz 12'de olculdu). Agac bilgisi bu yuzden gelen ayarlardan okunamaz;
/// sarmalayici onu <see cref="AgentPrismRunContext"/> kapsamindan okur ve
/// <see cref="AgentPrismRunOptions"/> nesnesini kendisi kurar.
/// </para>
/// </remarks>
public sealed class ChildAgentInvoker : AIAgent
{
    private readonly CallableAgentResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger _logger;
    private readonly string _callerName;
    private readonly string _childName;
    private readonly string? _childDescription;

    /// <summary>Yeni bir alt agent sarmalayicisi olusturur.</summary>
    /// <param name="resolver">Alt agent'i katalogdan cozen cozucu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="callerName">Cagiran agent'in adi.</param>
    /// <param name="child">Cagrilacak alt agent'in ozeti.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public ChildAgentInvoker(
        CallableAgentResolver resolver,
        ITenantContext tenantContext,
        ILogger logger,
        string callerName,
        CallableAgentInfo child)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(child.Name, nameof(child));

        _resolver = resolver;
        _tenantContext = tenantContext;
        _logger = logger;
        _callerName = callerName;
        _childName = child.Name;
        _childDescription = child.Description;
    }

    /// <inheritdoc />
    public override string Name => _childName;

    /// <inheritdoc />
    public override string? Description => _childDescription;

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scope = AgentPrismRunContext.Current;

        if (Refuse(scope) is { } refusal)
        {
            return new AgentResponse(new ChatMessage(ChatRole.Assistant, refusal));
        }

        var childOptions = CreateChildOptions(scope!);
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await agent
                .RunAsync(messages, session, childOptions, cancellationToken)
                .ConfigureAwait(false);

            return ChildRunApproval.Describe(response.Messages) is { } pending
                ? new AgentResponse(new ChatMessage(ChatRole.Assistant, ApprovalRefusal(pending)))
                : response;
        }
        finally
        {
            await WriteCompletedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scope = AgentPrismRunContext.Current;

        if (Refuse(scope) is { } refusal)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, refusal);
            yield break;
        }

        var childOptions = CreateChildOptions(scope!);
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        await WriteStartedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            var updates = agent.RunStreamingAsync(messages, session, childOptions, cancellationToken);

            await foreach (var update in updates.ConfigureAwait(false))
            {
                yield return ChildRunApproval.Describe(update.Contents) is { } pending
                    ? new AgentResponseUpdate(ChatRole.Assistant, ApprovalRefusal(pending))
                    : update;
            }
        }
        finally
        {
            await WriteCompletedAsync(scope!, childOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Cagrinin reddedilme sebebini uretir; cagri yapilabiliyorsa <see langword="null"/> doner.
    /// </summary>
    /// <remarks>
    /// Ret bir istisna degil, <em>tool sonucu</em> olarak doner. Istisna atmak
    /// cagiran agent'in calistirmasini basarisiz yapardi; oysa "bu alt cagriyi
    /// yapamadim" bilgisi modelin degerlendirip baska bir yol denemesi gereken
    /// normal bir sonuctur.
    /// </remarks>
    private string? Refuse(AgentRunScope? scope)
    {
        if (scope is null)
        {
            _logger.LogWarning(
                "'{Caller}' agent'i '{Child}' agent'ini cagirmak istedi ancak calistirma kapsami yok. " +
                "Alt cagri yalnizca AgentPrism'in calistirma kaydi acikken yapilabilir.",
                _callerName,
                _childName);

            return $"'{_childName}' agent'i cagirilamadi: calistirma kaydi kapali oldugu icin " +
                   "alt agent cagrilari devre disi.";
        }

        var maxDepth = scope.Budget?.MaxDepth ?? 0;

        if (scope.Depth + 1 > maxDepth)
        {
            return $"'{_childName}' agent'i cagirilamadi: cagri derinligi siniri asildi " +
                   $"(izin verilen en fazla derinlik {maxDepth}). Isi kendin tamamla veya " +
                   "daha az katmanli bir cagri zinciri kur.";
        }

        // Kiraci sizintisi tam burada olusur. Alt cagri baska bir is parcaciginda
        // calisir; kiraci baglami bir sekilde kaybolduysa varsayilan kiraciya
        // duserdi ve bir kiracinin agent'i baska bir kiracinin verisiyle calisirdi.
        if (!string.Equals(scope.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "'{Caller}' agent'inin '{Child}' cagrisi reddedildi: kiraci degisti " +
                "('{Expected}' -> '{Actual}').",
                _callerName,
                _childName,
                scope.TenantId,
                _tenantContext.TenantId);

            return $"'{_childName}' agent'i cagirilamadi: alt calistirma cagiranin kiracisindan cikamaz.";
        }

        if (scope.Budget is { } budget && !budget.TryReserveRun())
        {
            return $"'{_childName}' agent'i cagirilamadi: {budget.DescribeExhaustion()}";
        }

        return null;
    }

    private static AgentPrismRunOptions CreateChildOptions(AgentRunScope scope)
        => new()
        {
            RunId = AgentPrismId.NewId(),
            ParentRunId = scope.RunId,
            RootRunId = scope.RootRunId,
            Depth = scope.Depth + 1,

            // Ayni ORNEK tasinir. Kopyalanirsa her dal kendi butcesini alir.
            Budget = scope.Budget,

            // MAF alt agent'a bir oturum gecirmez. Oturum kimligi yine de tasinir:
            // alt calistirmada calisan bir tool'un urettigi ek kok oturuma aittir
            // ve oturumsuz yazilirsa saklama politikasi onu sahipsiz sayip siler.
            SessionId = scope.SessionId,
        };

    private ValueTask WriteStartedAsync(
        AgentRunScope scope,
        AgentPrismRunOptions childOptions,
        CancellationToken cancellationToken)
        => WriteAsync(scope, RunEventType.ChildRunStarted, childOptions, cancellationToken);

    private ValueTask WriteCompletedAsync(
        AgentRunScope scope,
        AgentPrismRunOptions childOptions,
        CancellationToken cancellationToken)
        => WriteAsync(scope, RunEventType.ChildRunCompleted, childOptions, CancellationToken.None);

    private async ValueTask WriteAsync(
        AgentRunScope scope,
        RunEventType type,
        AgentPrismRunOptions childOptions,
        CancellationToken cancellationToken)
    {
        if (scope.Writer is not { } writer)
        {
            return;
        }

        await writer.AppendAsync(
            new RunEventDraft(type)
            {
                Text = _childName,
                Payload = childOptions.RunId?.ToString(),
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AIAgent> ResolveAsync(CancellationToken cancellationToken)
    {
        var agent = await _resolver.ResolveAsync(_childName, cancellationToken).ConfigureAwait(false);

        return agent ?? throw new AgentPrismException(
            $"'{_callerName}' agent'i '{_childName}' agent'ini cagirmak istiyor ancak boyle bir " +
            "agent katalogda yok. Cagri grafigi kaydetme aninda dogrulanir; alt agent " +
            "sonradan silinmis olabilir.");
    }

    private string ApprovalRefusal(string toolNames)
        => $"'{_childName}' agent'i tamamlanamadi: '{toolNames}' tool'u kullanici onayi istiyor. " +
           "Alt agent onay isteyemez; onay bir sonraki turun girdisidir ve agacin ortasinda " +
           "beklenemez. Bu tool icin otomatik onay kurali tanimlayin veya alt agent'i " +
           "onay gerektirmeyen tool'larla sinirlayin.";
}

/// <summary>Bir yanitin onay bekleyen tool cagrisi tasiyip tasimadigini belirler.</summary>
/// <remarks>
/// Tespit tek bir yerde tutulur cunku iki tuketicisi vardir:
/// <see cref="ChildAgentInvoker"/> cagirana anlasilir bir hata metni dondurur,
/// <see cref="RunRecordingAgent"/> ise alt calistirmayi <c>Failed</c> olarak
/// kapatir. Iki yerde ayri ayri yazilsaydi biri degisip digeri kalirdi.
/// </remarks>
internal static class ChildRunApproval
{
    /// <summary>Mesajlarda onay bekleyen tool cagrisi arar.</summary>
    /// <param name="messages">Yanit mesajlari.</param>
    /// <returns>Onay bekleyen tool adlari; yoksa <see langword="null"/>.</returns>
    public static string? Describe(IEnumerable<ChatMessage> messages)
    {
        List<string>? names = null;

        foreach (var message in messages)
        {
            Collect(message.Contents, ref names);
        }

        return names is null ? null : string.Join(", ", names);
    }

    /// <summary>Iceriklerde onay bekleyen tool cagrisi arar.</summary>
    /// <param name="contents">Yanit icerikleri.</param>
    /// <returns>Onay bekleyen tool adlari; yoksa <see langword="null"/>.</returns>
    public static string? Describe(IEnumerable<AIContent> contents)
    {
        List<string>? names = null;

        Collect(contents, ref names);

        return names is null ? null : string.Join(", ", names);
    }

    private static void Collect(IEnumerable<AIContent> contents, ref List<string>? names)
    {
        foreach (var content in contents)
        {
            if (content is not ToolApprovalRequestContent request)
            {
                continue;
            }

            // Onay istegi her zaman bir fonksiyon cagrisi tasimayabilir; tool adi
            // yoksa cagri kimligi yazilir. Mesajin bos kalmasi, kullanicinin hangi
            // tool'un onay istedigini hic ogrenememesi demektir.
            (names ??= []).Add(request.ToolCall is FunctionCallContent call
                ? call.Name
                : request.ToolCall.CallId);
        }
    }
}
