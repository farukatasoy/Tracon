using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// AgentPrism'in HTTP yuzeyini uygulamaya baglayan uzantilar.
/// </summary>
public static class AgentPrismEndpointRouteBuilderExtensions
{
    /// <summary>Varsayilan yol oneki.</summary>
    public const string DefaultPrefix = "/agentprism";

    /// <summary>
    /// AgentPrism yonetim API'sini ve OpenAI uyumlu calistirma uclarini baglar.
    /// </summary>
    /// <param name="endpoints">Uygulamanin yonlendirme olusturucusu.</param>
    /// <param name="prefix">Yol oneki. Varsayilan <c>/agentprism</c>.</param>
    /// <param name="configure">Erisim ve akis ayarlarini degistiren kanca.</param>
    /// <returns>
    /// Korumali uclarin sozlesme olusturucusu. Eklenen her convention
    /// <strong>yalnizca</strong> korumali uclara uygulanir.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> bos ise.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddAgentPrism()</c> cagrilmamissa.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Uclar iki gruba ayrilir. <c>{prefix}/api/meta</c> kimlik dogrulamasi olmadan
    /// erisilir; arayuzun hangi kimlik yontemini kullanacagini ogrenmesi icin
    /// gereklidir ve hicbir hassas veri dondurmez. Diger tum uclar uc katmanli
    /// korumadan gecer.
    /// </para>
    /// <para>
    /// Donen olusturucu <strong>yalnizca korumali grubu</strong> temsil eder. Bunun
    /// sebebi bilincli: cagiran <c>MapAgentPrism(...).RequireAuthorization()</c>
    /// yazdiginda meta ucu de kilitlenseydi arayuz kimlik yontemini ogrenemez ve
    /// hicbir zaman oturum acamazdi.
    /// </para>
    /// <example>
    /// <code>
    /// app.MapAgentPrism("/agentprism", options =>
    /// {
    ///     options.RequireAuthorization("AgentPrismAdmin");
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    public static IEndpointConventionBuilder MapAgentPrism(
        this IEndpointRouteBuilder endpoints,
        string prefix = DefaultPrefix,
        Action<AgentPrismEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var options = new AgentPrismEndpointOptions();
        configure?.Invoke(options);

        var services = endpoints.ServiceProvider;

        if (services.GetService<IAgentCatalog>() is null)
        {
            throw new InvalidOperationException(
                "AgentPrism servisleri kayitli degil. MapAgentPrism() cagrisindan once " +
                "builder.AddAgentPrism() (veya services.AddAgentPrism()) cagirin.");
        }

        var normalizedPrefix = '/' + prefix.Trim('/');

        // Rol policy'lerinin kayit durumu bir kez cozulur. Bir policy kayitli
        // degilse ilgili alan null'dir ve RequireRole hicbir sey eklemez — uc
        // yalnizca uc katmanli korumadan gecer (eski davranis).
        var roles = AgentPrismRolePolicies.Resolve(services, options);

        // Meta grubu: kimlik dogrulamasi yok, filtre yok.
        var metaGroup = endpoints.MapGroup(normalizedPrefix).WithTags("AgentPrism");
        MetaEndpoints.Map(metaGroup, options, normalizedPrefix, roles);

        // Idempotency-Key destegi (Faz 43). Govde, filtrenin InvokeAsync'inden
        // ONCE (bazi uclarda minimal API'nin otomatik baglamasi tarafindan)
        // tuketilebildigi icin ham baytlarin sonradan okunabilmesi ancak
        // ONCEDEN tamponlanmisilarsa mumkundur. Baslik TASIMAYAN bir istek icin
        // bu ara yazilim hicbir sey yapmaz (K1: sessiz maliyet yoktur).
        if (endpoints is IApplicationBuilder idempotencyApp)
        {
            idempotencyApp.Use(static (httpContext, next) =>
            {
                if (httpContext.Request.Headers.ContainsKey(IdempotencyFilter.HeaderName))
                {
                    httpContext.Request.EnableBuffering();
                }

                return next(httpContext);
            });
        }

        var idempotencyFilter = new IdempotencyFilter(
            services.GetRequiredService<IOptionsMonitor<AgentPrismIdempotencyOptions>>());

        // Korumali grup: loopback + bearer token filtresi, istege bagli policy.
        var group = endpoints.MapGroup(normalizedPrefix).WithTags("AgentPrism");
        group.AddEndpointFilter(new AgentPrismEndpointFilter(options));

        // Hiz siniri (Faz 21). Filtre her zaman eklenir ama VARSAYILAN KAPALIDIR:
        // ayar acilmadikca hicbir istek reddedilmez ve mevcut kurulumlar
        // yukseltmeden sonra beklenmedik 429 gormez (K-165). Filtre yalnizca
        // AgentPrism'in kendi uc grubuna uygulanir; tuketicinin AddRateLimiter()
        // ile kurdugu genel sinirla yarismaz.
        if (services.GetService<IOptionsMonitor<AgentPrismRateLimitOptions>>() is { } rateLimitOptions)
        {
            group.AddEndpointFilter(new AgentPrismRateLimitFilter(rateLimitOptions));
        }

        if (options.AuthorizationPolicy is { Length: > 0 } policy)
        {
            group.RequireAuthorization(policy);
        }

        AgentEndpoints.Map(group, roles, normalizedPrefix, idempotencyFilter);
        AttachmentEndpoints.Map(group, roles);
        SkillEndpoints.Map(group, roles);
        SkillScriptGrantEndpoints.Map(group, roles);
        SessionEndpoints.Map(group, roles);
        RunEndpoints.Map(group, options, roles, normalizedPrefix);
        WorkflowEndpoints.Map(group, roles);
        SchedulingEndpoints.Map(group, roles);
        EvalEndpoints.Map(group, roles);
        ExperimentEndpoints.Map(group, roles);
        CatalogEndpoints.Map(group, roles);
        QuotaEndpoints.Map(group, roles);
        WebhookEndpoints.Map(group, roles);
        RetentionEndpoints.Map(group, roles);
        ModelHealthEndpoints.Map(group, roles);
        VoiceEndpoints.Map(group, roles);
        ObservabilityEndpoints.Map(group, roles);
        GovernanceEndpoints.Map(group, roles);
        AuditEndpoints.Map(group, roles);

        if (options.EnableDiagnosticsEndpoint)
        {
            DiagnosticsEndpoints.Map(group, services, roles);
        }

        OpenAIResponsesEndpoints.Map(group, ResolveSessionStore(services), roles, normalizedPrefix, idempotencyFilter);
        OpenAIChatCompletionsEndpoints.Map(group, roles, idempotencyFilter);
        OpenAIConversationsEndpoints.Map(group, roles);

        MapUi(endpoints, services, options, normalizedPrefix);
        MapMcpOAuthCallback(endpoints, options, normalizedPrefix);
        MapVoiceConversation(endpoints, services, options, normalizedPrefix, roles);

        return group;
    }

    /// <summary>
    /// Konusma katmani aciksa WebSocket ucunu baglar ve tasima ara yazilimini
    /// kurar.
    /// </summary>
    /// <param name="endpoints">Uygulamanin yonlendirme olusturucusu.</param>
    /// <param name="services">Servis saglayici.</param>
    /// <param name="options">Erisim ayarlari.</param>
    /// <param name="prefix">Normalize edilmis yol oneki.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    /// <remarks>
    /// <para>
    /// <c>UseVoiceConversation()</c> cagrilmadiysa <see cref="VoiceConversationDriver"/>
    /// kayitli degildir ve <strong>hicbir sey baglanmaz</strong>: ne uc, ne ara
    /// yazilim. Barindirma modelini degistiren bir yetenek sessizce acilmaz.
    /// </para>
    /// <para>
    /// 🚨 <c>UseWebSockets()</c> BURADA cagrilir. Kestrel <c>IHttpWebSocketFeature</c>
    /// saglamaz; onu <c>WebSocketMiddleware</c> kurar. Tuketiciden ayrica bir
    /// cagri istemek <c>MapAgentPrism</c>'in tek giris noktasi olma kuralini
    /// bozardi ve hata yalnizca ilk konusma denemesinde gorunurdu. Ara yazilim
    /// zaten kuruluysa ikinci ornek <c>IHttpWebSocketFeature</c>'i dolu bulur ve
    /// dokunmadan gecer.
    /// </para>
    /// <para>
    /// Uc <strong>ayri bir gruba</strong> baglanir: tarayici bir WebSocket el
    /// sikismasina <c>Authorization</c> basligi ekleyemez, token alt protokolde
    /// tasinir ve ucun kendisi dogrular. Ayrinti:
    /// <see cref="VoiceConversationEndpoint"/>.
    /// </para>
    /// </remarks>
    private static void MapVoiceConversation(
        IEndpointRouteBuilder endpoints,
        IServiceProvider services,
        AgentPrismEndpointOptions options,
        string prefix,
        AgentPrismRolePolicies roles)
    {
        if (services.GetService<VoiceConversationDriver>() is null)
        {
            return;
        }

        if (endpoints is IApplicationBuilder app)
        {
            app.UseWebSockets();
        }

        var voiceGroup = endpoints.MapGroup(prefix).WithTags("AgentPrism");
        voiceGroup.AddEndpointFilter(new AgentPrismEndpointFilter(options, requireBearerToken: false));

        if (options.AuthorizationPolicy is { Length: > 0 } policy)
        {
            voiceGroup.RequireAuthorization(policy);
        }

        VoiceConversationEndpoint.Map(voiceGroup, options, roles);
    }

    /// <summary>
    /// Kayitli bir arayuz kaynagi varsa statik varlik rotalarini baglar.
    /// </summary>
    /// <param name="endpoints">Uygulamanin yonlendirme olusturucusu.</param>
    /// <param name="services">Servis saglayici.</param>
    /// <param name="options">Erisim ayarlari.</param>
    /// <param name="prefix">Normalize edilmis yol oneki.</param>
    /// <remarks>
    /// <para>
    /// Arayuz ucuncu bir gruba baglanir. Sebebi guvenlik katmanlarinin farkli
    /// olmasidir: kabuk loopback kisitindan ve authorization policy'den gecer,
    /// ancak bearer token denetiminden muaftir. Ayrinti:
    /// <see cref="AgentPrismEndpointFilter"/>.
    /// </para>
    /// <para>
    /// Kayit yoksa hicbir rota eklenmez. <c>AgentPrism.UI</c> paketi kurulu
    /// degilse veya <c>UseUI()</c> cagrilmamissa HTTP yuzeyi degismez.
    /// </para>
    /// </remarks>
    private static void MapUi(
        IEndpointRouteBuilder endpoints,
        IServiceProvider services,
        AgentPrismEndpointOptions options,
        string prefix)
    {
        if (services.GetService<IAgentPrismUiProvider>() is not { HasAssets: true } provider)
        {
            return;
        }

        var uiGroup = endpoints.MapGroup(prefix).WithTags("AgentPrism");
        uiGroup.AddEndpointFilter(new AgentPrismEndpointFilter(options, requireBearerToken: false));

        if (options.AuthorizationPolicy is { Length: > 0 } policy)
        {
            uiGroup.RequireAuthorization(policy);
        }

        UiEndpoints.Map(uiGroup, provider, prefix);
    }

    /// <summary>
    /// OAuth Mod 1 geri donus (callback) ucunu baglar.
    /// </summary>
    /// <remarks>
    /// Ayri bir gruba baglanir: saglayicinin yonlendirdigi tarayici istegi bizim
    /// bearer token'imizi tasiyamaz — <see cref="AgentPrismEndpointFilter"/>
    /// <c>requireBearerToken: false</c> ile kurulur, tipki arayuz kabugu gibi
    /// (<see cref="MapUi"/>). Loopback kisiti ve authorization policy yine de
    /// uygulanir; guvenligin asil kaynagi tek kullanimlik <c>state</c> degeridir
    /// (bolum 22.3).
    /// </remarks>
    private static void MapMcpOAuthCallback(IEndpointRouteBuilder endpoints, AgentPrismEndpointOptions options, string prefix)
    {
        var callbackGroup = endpoints.MapGroup(prefix).WithTags("AgentPrism");
        callbackGroup.AddEndpointFilter(new AgentPrismEndpointFilter(options, requireBearerToken: false));

        if (options.AuthorizationPolicy is { Length: > 0 } policy)
        {
            callbackGroup.RequireAuthorization(policy);
        }

        GovernanceEndpoints.MapMcpOAuthCallback(callbackGroup);
    }

    /// <summary>
    /// OpenAI uyumlu uclarin kullanacagi oturum deposunu secer.
    /// </summary>
    /// <param name="services">Uygulamanin servis saglayicisi.</param>
    /// <returns>Kullanilacak oturum deposu.</returns>
    /// <remarks>
    /// <para>
    /// Tuketici kendi <see cref="AgentSessionStore"/> uygulamasini kaydettiyse o
    /// kazanir (kural K4). Ornegin cok kiracili bir kurulum, MAF'in
    /// <c>IsolationKeyScopedAgentSessionStore</c> sinifiyla sarmalanmis bir depo
    /// kaydedebilir.
    /// </para>
    /// <para>
    /// Kayit yoksa <see cref="AgentSessionManager"/> uzerine kurulu varsayilan
    /// kopru kullanilir. Boylece <c>MapAgentPrism()</c> tek giris noktasi olarak
    /// kalir ve ek bir kayit adimi gerekmez (kural K1).
    /// </para>
    /// </remarks>
    private static AgentSessionStore ResolveSessionStore(IServiceProvider services)
        => services.GetService<AgentSessionStore>()
           ?? new AgentPrismAgentSessionStore(services.GetRequiredService<AgentSessionManager>());
}
