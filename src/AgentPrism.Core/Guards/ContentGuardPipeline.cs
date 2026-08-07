using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kayitli <see cref="IContentGuard"/> uygulamalarini sirayla calistiran ve
/// kararlarini kaydeden boru hatti.
/// </summary>
/// <remarks>
/// <para>
/// Guard'lar kayit sirasinda calisir ve <strong>en sert karar kazanir</strong>:
/// bir guard <see cref="ContentGuardAction.Mask"/> dondururse metin degistirilir
/// ve <em>degistirilmis hali</em> sonraki guard'a verilir; bir guard
/// <see cref="ContentGuardAction.Block"/> dondururse zincir hemen kesilir. Block
/// taksonominin en buyuk degeri oldugu icin sonuc kayit sirasindan bagimsizdir.
/// </para>
/// <para>
/// 🚨 <strong>Bir guard istisna atarsa calistirma basarisiz olur.</strong>
/// "Gozlemlenebilirlik islevselligi bozmaz" kurali burada gecerli degildir: guard
/// bir gozlem araci degil bir kontroldur ve denetlenemeyen icerik gecirilmez
/// (K-089'un ayni gerekcesi).
/// </para>
/// <para>
/// Karar <em>kaydi</em> ise o kurala tabidir: olay yazicisi veya denetim izi hata
/// verirse hata loglanir ve karar yine uygulanir. Kararin kendisi kaybolmaz.
/// </para>
/// </remarks>
public sealed class ContentGuardPipeline
{
    private readonly IContentGuard[] _guards;
    private readonly IOptionsMonitor<AgentPrismContentGuardOptions> _options;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger _logger;

    /// <summary>Yeni bir boru hatti olusturur.</summary>
    /// <param name="guards">Kayitli guard'lar. Bos olabilir.</param>
    /// <param name="options">Boru hatti ayarlari.</param>
    /// <param name="auditLog">Engelleme kararlarinin yazilacagi denetim izi.</param>
    /// <param name="actorResolver">Denetim izi aktor cozumleyici.</param>
    /// <param name="tenantContext">Calistirma kapsami yoksa kullanilacak kiraci baglami.</param>
    /// <param name="loggerFactory">Gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public ContentGuardPipeline(
        IEnumerable<IContentGuard> guards,
        IOptionsMonitor<AgentPrismContentGuardOptions> options,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _guards = [.. guards];
        _options = options;
        _auditLog = auditLog;
        _actorResolver = actorResolver;
        _tenantContext = tenantContext;
        _logger = loggerFactory.CreateLogger<ContentGuardPipeline>();
    }

    /// <summary>
    /// En az bir guard kayitli mi.
    /// </summary>
    /// <remarks>
    /// 🚨 <see langword="false"/> ise <c>ModelProviderRegistry</c> denetim
    /// sarmalayicisini boru hattina <strong>hic eklemez</strong>: model cagrisi
    /// yolunda tek bir <c>if</c> bile calismaz.
    /// </remarks>
    public bool HasGuards => _guards.Length > 0;

    /// <summary>Boru hattinin guncel ayarlari.</summary>
    public AgentPrismContentGuardOptions Options => _options.CurrentValue;

    /// <summary>Bir metin parcasini butun guard'lardan gecirir.</summary>
    /// <param name="direction">Denetimin yonu.</param>
    /// <param name="text">Denetlenecek metin.</param>
    /// <param name="modelId">Cagrilan modelin kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Maskelenmis metin; hicbir guard degisiklik istemediyse <see langword="null"/>.
    /// <see langword="null"/> donmesi cagiranin hicbir sey yeniden kurmamasi
    /// gerektigini soyler ve tahsissiz yoldur.
    /// </returns>
    /// <exception cref="AgentPrismContentBlockedException">
    /// Bir guard <see cref="ContentGuardAction.Block"/> dondurduyse.
    /// </exception>
    public async ValueTask<string?> InspectAsync(
        ContentGuardDirection direction,
        string text,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var scope = AgentPrismRunContext.Current;
        var current = text;
        string? masked = null;

        foreach (var guard in _guards)
        {
            var context = new ContentGuardContext
            {
                Direction = direction,
                Text = current,
                RunId = scope?.RunId,
                TenantId = scope?.TenantId ?? _tenantContext.TenantId,
                AgentName = scope?.AgentName,
                ModelId = modelId,
            };

            var result = await guard.InspectAsync(context, cancellationToken).ConfigureAwait(false);

            switch (result.Action)
            {
                case ContentGuardAction.Block:
                    await RecordAsync(guard, result, direction, scope, cancellationToken).ConfigureAwait(false);

                    throw new AgentPrismContentBlockedException(
                        $"Icerik '{guard.Name}' guard'i tarafindan engellendi " +
                        $"(kural: {result.RuleName ?? "bilinmiyor"}, yon: {direction}). " +
                        (result.Reason ?? "Sebep bildirilmedi.") +
                        " Engellenen metin bilerek kaydedilmiyor.")
                    {
                        GuardName = guard.Name,
                        RuleName = result.RuleName,
                        Direction = direction,
                    };

                case ContentGuardAction.Mask when result.MaskedText is { } replacement:
                    await RecordAsync(guard, result, direction, scope, cancellationToken).ConfigureAwait(false);
                    current = replacement;
                    masked = replacement;
                    break;

                default:
                    break;
            }
        }

        return masked;
    }

    /// <summary>
    /// Karari calistirma olayina, engellemeyi ayrica denetim izine yazar.
    /// </summary>
    /// <remarks>
    /// 🚨 Yazilan sey <strong>icerik degildir</strong>: guard adi, kural adi ve yon.
    /// Engellenen icerik tanimi geregi hassastir; onu bir ize yazmak sorunu kalici
    /// hale getirir (K-059'un ruhu, <c>AuditSecretFilter</c> ile ayni yon).
    /// </remarks>
    private async ValueTask RecordAsync(
        IContentGuard guard,
        ContentGuardResult result,
        ContentGuardDirection direction,
        AgentRunScope? scope,
        CancellationToken cancellationToken)
    {
        var blocked = result.Action == ContentGuardAction.Block;
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{guard.Name}/{result.RuleName ?? "unknown"} ({direction})");

        if (scope?.Writer is { } writer)
        {
            await writer.AppendAsync(
                new RunEventDraft(blocked ? RunEventType.ContentBlocked : RunEventType.ContentMasked)
                {
                    Text = summary,
                    Payload = Describe(guard, result, direction),
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (!blocked)
        {
            return;
        }

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            scope?.TenantId ?? _tenantContext.TenantId,
            action: "content.blocked",
            entity: scope is { } run
                ? string.Create(CultureInfo.InvariantCulture, $"run:{run.RunId}")
                : "run:unknown",
            before: null,
            after: Describe(guard, result, direction),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Karari JSON olarak anlatir. 🚨 Icerik tasimaz.</summary>
    /// <remarks>
    /// Elle bicimlendirilir: <c>AgentPrism.Core</c> AOT uyumludur ve bu kadar kucuk
    /// bir nesne icin bir <c>JsonSerializerContext</c> girisi acmak gereksizdir
    /// (ayni gerekce tool argumani bicimlendirmesinde de kullanildi).
    /// </remarks>
    private static string Describe(IContentGuard guard, ContentGuardResult result, ContentGuardDirection direction)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"guard\":\"{Escape(guard.Name)}\",\"rule\":\"{Escape(result.RuleName)}\",\"direction\":\"{direction}\",\"action\":\"{result.Action}\"}}");

    private static string Escape(string? value)
        => value is null ? string.Empty : value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
