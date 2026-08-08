using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kiraciyi gecerli HTTP isteginden cozer.
/// </summary>
/// <remarks>
/// <para>
/// Sinif <strong>singleton</strong>'dir ve <see cref="IHttpContextAccessor"/>
/// uzerinden calisir. Kapsamli (scoped) bir uygulama, <see cref="ITenantContext"/>
/// enjekte eden singleton servislerde (depolar, dekoratorler) yakalanmis bagimlilik
/// uretirdi ve ASP.NET Core bunu baslangicta hata olarak bildirirdi.
/// </para>
/// <para>
/// Cozum sirasi:
/// </para>
/// <list type="number">
///   <item><description>Cok kiracililik kapaliysa → varsayilan kiraci.</description></item>
///   <item><description>Claim tipi ayarli ve kullanici kimlik dogrulamasindan gectiyse → claim.</description></item>
///   <item><description>Baslik cozumu aciksa → baslik.</description></item>
///   <item><description>Hicbiri yoksa → varsayilan kiraci.</description></item>
/// </list>
/// <para>
/// 🚨 Claim ayarliyken <strong>baslik hic okunmaz</strong>. Aksi halde kimlik
/// dogrulamasindan gecmis bir kullanici, bir baslik ekleyerek baska bir kiracinin
/// verisine erisebilirdi.
/// </para>
/// </remarks>
public sealed partial class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IOptions<AgentPrismOptions> _coreOptions;
    private readonly IOptions<AgentPrismTenancyOptions> _tenancyOptions;

    /// <summary>Yeni bir HTTP kiraci baglami olusturur.</summary>
    /// <param name="accessor">Gecerli istegi cozen erisimci.</param>
    /// <param name="coreOptions">AgentPrism ayarlari.</param>
    /// <param name="tenancyOptions">Kiraci cozumleme ayarlari.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public HttpTenantContext(
        IHttpContextAccessor accessor,
        IOptions<AgentPrismOptions> coreOptions,
        IOptions<AgentPrismTenancyOptions> tenancyOptions)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(tenancyOptions);

        _accessor = accessor;
        _coreOptions = coreOptions;
        _tenancyOptions = tenancyOptions;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <see cref="AmbientTenantScope.Current"/> ayarliysa (zamanlanmis bir is
    /// yurutuluyorsa, HTTP baglami yoktur) o deger HTTP cozumlemesine tercih
    /// edilir.
    /// </para>
    /// <para>
    /// 🚨 Bir API anahtariyla dogrulanmis bir istekte kiraci ANAHTARIN
    /// <c>tenant_id</c>'sinden cozulur — bu, claim veya baslikten ONCE gelir
    /// (bolum 53.5). Anahtar bir sirri KANITLAR; baslik yalnizca istemcinin
    /// BEYANIDIR. <see cref="AgentPrismEndpointFilter"/> baslik anahtarin
    /// kiracisiyla celisirse istegi zaten 403 ile reddeder, dolayisiyla bu
    /// noktaya ulasan bir istekte ikisi ya eslesir ya da baslik hic yoktur.
    /// </para>
    /// </remarks>
    public string TenantId =>
        AmbientTenantScope.Current ?? ResolveFromApiKey() ?? Resolve() ?? _coreOptions.Value.DefaultTenantId;

    /// <summary>
    /// Bir kiraci kimliginin bicimce gecerli olup olmadigini soyler.
    /// </summary>
    /// <param name="tenantId">Denetlenecek deger.</param>
    /// <returns>Deger kabul edilebilirse <see langword="true"/>.</returns>
    /// <remarks>
    /// Kimlik veritabaninda bir metin sutunudur ve sorgu parametresi olarak gider;
    /// SQL enjeksiyonu mumkun degildir. Bicim kisiti yine de vardir: kontrolsuz
    /// bir deger gunluklere, denetim izine ve arayuze oldugu gibi yansirdi.
    /// </remarks>
    public static bool IsValidTenantId(string? tenantId)
        => !string.IsNullOrWhiteSpace(tenantId)
            && tenantId.Length <= 64
            && TenantIdPattern().IsMatch(tenantId);

    private string? ResolveFromApiKey()
        => _accessor.HttpContext is { } context && ApiKeyRequestContext.Get(context) is { } record
            ? record.TenantId
            : null;

    private string? Resolve()
    {
        var options = _tenancyOptions.Value;

        if (!options.Enabled || _accessor.HttpContext is not { } context)
        {
            return null;
        }

        // Claim ayarliysa baslik HIC okunmaz.
        if (options.ClaimType is { Length: > 0 } claimType)
        {
            return context.User.Identity?.IsAuthenticated == true
                ? Accept(context.User.FindFirst(claimType)?.Value, options)
                : null;
        }

        if (!options.AllowHeaderResolution)
        {
            return null;
        }

        return Accept(context.Request.Headers[options.HeaderName].ToString(), options);
    }

    private static string? Accept(string? candidate, AgentPrismTenancyOptions options)
    {
        if (!IsValidTenantId(candidate))
        {
            return null;
        }

        if (options.AllowedTenants.Count == 0)
        {
            return candidate;
        }

        // Beyaz liste doluysa disindaki bir deger varsayilan kiraciya DUSMEZ;
        // dusmek, yetkisiz bir istegin varsayilan kiracinin verisini gormesi demekti.
        return options.AllowedTenants.Contains(candidate, StringComparer.Ordinal) ? candidate : null;
    }

    [GeneratedRegex("^[a-zA-Z0-9_.-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TenantIdPattern();
}
