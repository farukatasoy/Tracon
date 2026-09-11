using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Extensions that connect AgentPrism's HTTP surface to the application.
/// </summary>
public static class AgentPrismEndpointRouteBuilderExtensions
{
    /// <summary>Default path prefix.</summary>
    public const string DefaultPrefix = "/agentprism";

    /// <summary>
    /// Connects the AgentPrism management API and the OpenAI-compatible run
    /// endpoints.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="prefix">Path prefix. Default is <c>/agentprism</c>.</param>
    /// <param name="configure">Hook that mutates access and streaming settings.</param>
    /// <returns>
    /// The convention builder for the protected endpoints. Every convention
    /// added <strong>only</strong> applies to the protected endpoints.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddAgentPrism()</c> has not been called.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Endpoints fall into two groups. <c>{prefix}/api/meta</c> is reachable
    /// without authentication; it is needed for the UI to learn which
    /// authentication method to use and returns no sensitive data. Every other
    /// endpoint passes through the three-layer protection.
    /// </para>
    /// <para>
    /// The returned builder represents <strong>only the protected group</strong>.
    /// This is deliberate: if the meta endpoint were also locked when the
    /// caller writes <c>MapAgentPrism(...).RequireAuthorization()</c>, the UI
    /// could never learn the authentication method and could never open a
    /// session.
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
                "AgentPrism services are not registered. Call builder.AddAgentPrism() " +
                "(or services.AddAgentPrism()) before calling MapAgentPrism().");
        }

        var normalizedPrefix = '/' + prefix.Trim('/');

        // Registration status of the role policies is resolved once. If a
        // policy is not registered the corresponding field is null and
        // RequireRole adds nothing — the endpoint only passes through the
        // three-layer protection (legacy behavior).
        var roles = AgentPrismRolePolicies.Resolve(services, options);

        // Meta group: no authentication, no filter.
        var metaGroup = endpoints.MapGroup(normalizedPrefix).WithTags("AgentPrism");
        MetaEndpoints.Map(metaGroup, options, normalizedPrefix, roles);

        // CORS (Phase 61). Off by default (AllowedOrigins is empty); added
        // before the idempotency/JSON-binding middleware below so a preflight
        // OPTIONS request (which never reaches a route handler) still gets a
        // CORS response.
        AgentPrismCorsMiddleware.Map(endpoints, options, normalizedPrefix);

        // Idempotency-Key support (Phase 43). Because the body can be consumed
        // BEFORE the filter's InvokeAsync (by minimal API's automatic binding
        // on some endpoints), reading the raw bytes afterward is only possible
        // if they were buffered BEFOREHAND. For a request that does NOT carry
        // the header, this middleware does nothing (K1: no silent cost).
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

        // HATA-S2-006/HATA-S2-007: when body binding hits a JsonException it
        // must fall into this library's own 400 contract, not a generic 500 —
        // see JsonBindingProblemMiddleware.
        if (endpoints is IApplicationBuilder jsonProblemApp)
        {
            jsonProblemApp.Use(JsonBindingProblemMiddleware.InvokeAsync);
        }

        var idempotencyFilter = new IdempotencyFilter(
            services.GetRequiredService<IOptionsMonitor<AgentPrismIdempotencyOptions>>());

        // Protected group: loopback + bearer token filter, optional policy.
        var group = endpoints.MapGroup(normalizedPrefix).WithTags("AgentPrism");
        group.AddEndpointFilter(new AgentPrismEndpointFilter(options));

        // Rate limiting (Phase 21). The filter is always added but is OFF BY
        // DEFAULT: until the setting is turned on no request is rejected, so
        // existing setups do not see an unexpected 429 after an upgrade
        // (K-165). The filter applies only to AgentPrism's own endpoint group;
        // it does not compete with a consumer's general limit set up via
        // AddRateLimiter().
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
        ApiKeyEndpoints.Map(group, roles);
        TenantProviderEndpoints.Map(group, roles);
        TriggerEndpoints.Map(group, roles);
        ApprovalEndpoints.Map(group, roles);
        RetentionEndpoints.Map(group, roles);
        KnowledgeEndpoints.Map(group, roles);
        ModelHealthEndpoints.Map(group, roles);
        VoiceEndpoints.Map(group, roles);

        // 🚨 Mapped only while UseLiveVoice() registered the launcher. A capability
        // that changes the hosting model must not appear as "present but off": with
        // the call absent the address does not exist (404), and only a missing
        // PROVIDER produces the 501.
        if (services.GetService<LiveVoiceSessionLauncher>() is not null)
        {
            LiveVoiceEndpoints.Map(group, roles);
        }

        if (services.GetRequiredService<IOptions<AgentPrismImageOptions>>().Value.Enabled)
        {
            // Resolve eagerly: enabled image generation without a provider must
            // fail while the HTTP surface is composed, not on its first paid call.
            _ = services.GetRequiredService<ImageGeneratorResolver>().Resolve();
            ImageEndpoints.Map(group, roles);
        }

        ObservabilityEndpoints.Map(group, roles);
        GovernanceEndpoints.Map(group, roles);
        AuditEndpoints.Map(group, roles);
        DataSubjectEndpoints.Map(group, roles);

        if (options.EnableDiagnosticsEndpoint)
        {
            DiagnosticsEndpoints.Map(group, services, roles);
        }

        OpenAIResponsesEndpoints.Map(group, ResolveSessionStore(services), roles, normalizedPrefix, idempotencyFilter);
        OpenAIChatCompletionsEndpoints.Map(group, roles, idempotencyFilter);
        if (options.MapOpenAIConversations)
        {
            OpenAIConversationsEndpoints.Map(group, roles);
        }

        MapUi(endpoints, services, options, normalizedPrefix);
        MapMcpOAuthCallback(endpoints, options, normalizedPrefix);
        MapInboundTriggerAccept(endpoints, options, normalizedPrefix);
        MapVoiceConversation(endpoints, services, options, normalizedPrefix, roles);

        // Phase 50: MapAgentPrismMcpServer/MapAgentPrismA2A are separate,
        // optional endpoints but MUST use the SAME access protection
        // (loopback + bearer + policy). Registering with IServiceCollection is
        // NOT POSSIBLE (the app is already Build()-ed); IApplicationBuilder.Properties
        // is therefore used for shared state — an extension called later on
        // the same `app` can read this instance back.
        StoreSharedEndpointOptions(endpoints, options);

        return group;
    }

    private const string SharedEndpointOptionsKey = "AgentPrism.SharedEndpointOptions";

    private static void StoreSharedEndpointOptions(IEndpointRouteBuilder endpoints, AgentPrismEndpointOptions options)
    {
        if (endpoints is IApplicationBuilder app)
        {
            app.Properties[SharedEndpointOptionsKey] = options;
        }
    }

    /// <summary>
    /// Reads back the access settings registered by <c>MapAgentPrism</c>. The
    /// MCP/A2A external surfaces inherit access protection (loopback, bearer
    /// token, authorization policy) from here; a second settings set is not built.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="protocol">Name of the calling external surface (for the error message).</param>
    /// <exception cref="InvalidOperationException"><c>MapAgentPrism</c> has not been called yet.</exception>
    internal static AgentPrismEndpointOptions RequireSharedEndpointOptions(IEndpointRouteBuilder endpoints, string protocol)
    {
        if (endpoints is IApplicationBuilder app &&
            app.Properties.TryGetValue(SharedEndpointOptionsKey, out var value) &&
            value is AgentPrismEndpointOptions options)
        {
            return options;
        }

        throw new InvalidOperationException(
            $"app.MapAgentPrism(...) must be called before exposing {protocol}. " +
            "Access protection (loopback, bearer token, authorization policy) is inherited from there.");
    }

    /// <summary>
    /// Connects the WebSocket endpoint and sets up the transport middleware
    /// when the voice conversation layer is enabled.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="services">Service provider.</param>
    /// <param name="options">Access settings.</param>
    /// <param name="prefix">Normalized path prefix.</param>
    /// <param name="roles">Resolved role policies.</param>
    /// <remarks>
    /// <para>
    /// If <c>UseVoiceConversation()</c> was not called, <see cref="VoiceConversationDriver"/>
    /// is not registered and <strong>nothing is connected</strong>: neither the
    /// endpoint nor the middleware. A capability that changes the hosting model
    /// is not silently enabled.
    /// </para>
    /// <para>
    /// <c>UseWebSockets()</c> is called HERE. Kestrel does not provide
    /// <c>IHttpWebSocketFeature</c>; <c>WebSocketMiddleware</c> sets it up.
    /// Requiring a separate call from the consumer would break
    /// <c>MapAgentPrism</c>'s rule of being the single entry point, and the
    /// error would only surface on the first conversation attempt. If the
    /// middleware is already installed, a second instance finds
    /// <c>IHttpWebSocketFeature</c> already set and passes through untouched.
    /// </para>
    /// <para>
    /// The endpoint is connected to a <strong>separate group</strong>: a
    /// browser cannot attach an <c>Authorization</c> header to a WebSocket
    /// handshake, so the token is carried in the sub-protocol and validated by
    /// the endpoint itself. Details: <see cref="VoiceConversationEndpoint"/>.
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
    /// Connects the static asset routes when a UI resource is registered.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="services">Service provider.</param>
    /// <param name="options">Access settings.</param>
    /// <param name="prefix">Normalized path prefix.</param>
    /// <remarks>
    /// <para>
    /// The UI is connected to a third group. The reason is that its security
    /// layers differ: the shell passes through the authorization policy, but
    /// is exempt from the bearer token check AND the loopback restriction — if
    /// either blocked the shell from downloading its JS bundle,
    /// <c>AccessGate</c> itself could never run. The shell
    /// carries no data; the real protection comes from the filter instances on
    /// the data endpoints (where the loopback restriction is on by default).
    /// Details: <see cref="AgentPrismEndpointFilter"/>.
    /// </para>
    /// <para>
    /// If nothing is registered, no route is added. If the <c>AgentPrism.UI</c>
    /// package is not installed or <c>UseUI()</c> was not called, the HTTP
    /// surface is unchanged.
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
        uiGroup.AddEndpointFilter(new AgentPrismEndpointFilter(options, requireBearerToken: false, requireLoopback: false));

        if (options.AuthorizationPolicy is { Length: > 0 } policy)
        {
            uiGroup.RequireAuthorization(policy);
        }

        UiEndpoints.Map(uiGroup, provider, prefix);
    }

    /// <summary>
    /// Connects the OAuth Mode 1 callback endpoint.
    /// </summary>
    /// <remarks>
    /// Connected to a separate group: the browser request redirected by the
    /// provider cannot carry our bearer token — <see cref="AgentPrismEndpointFilter"/>
    /// is set up with <c>requireBearerToken: false</c>, just like the UI shell
    /// (<see cref="MapUi"/>). The loopback restriction and authorization policy
    /// still apply; the real source of security is the single-use <c>state</c>
    /// value.
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
    /// Connects the inbound trigger accept endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Connected to its own group, the same pattern as <see cref="MapMcpOAuthCallback"/>
    /// and <see cref="MapUi"/>: <c>requireBearerToken: false</c> because the
    /// caller (an external system such as Slack) cannot present our bearer
    /// token, and <c>requireLoopback: false</c> because, unlike the OAuth
    /// callback, the caller is NOT the operator's own browser — it is a
    /// third-party server reaching in from the open internet by design.
    /// </para>
    /// <para>
    /// Deliberately does NOT apply <see cref="AgentPrismEndpointOptions.AuthorizationPolicy"/>,
    /// unlike every other <c>requireBearerToken: false</c> group: a consumer's
    /// ASP.NET Core authorization policy is normally satisfied by an
    /// interactive human (SSO, a cookie) — a webhook sender can never
    /// complete that challenge. Applying it here would let turning on SSO for
    /// the admin console silently break every inbound trigger at the same
    /// time. The HMAC signature IS this endpoint's complete authentication
    /// story; it does not layer under a second one.
    /// </para>
    /// </remarks>
    private static void MapInboundTriggerAccept(IEndpointRouteBuilder endpoints, AgentPrismEndpointOptions options, string prefix)
    {
        var triggerGroup = endpoints.MapGroup(prefix).WithTags("AgentPrism");
        triggerGroup.AddEndpointFilter(new AgentPrismEndpointFilter(options, requireBearerToken: false, requireLoopback: false));

        TriggerEndpoints.MapAccept(triggerGroup, prefix);
    }

    /// <summary>
    /// Selects the session store used by the OpenAI-compatible endpoints.
    /// </summary>
    /// <param name="services">The application's service provider.</param>
    /// <returns>The session store to use.</returns>
    /// <remarks>
    /// <para>
    /// If the consumer registered their own <see cref="AgentSessionStore"/>
    /// implementation, it wins (the replaceable-extension rule). For example a multi-tenant setup
    /// might register a store wrapped with MAF's
    /// <c>IsolationKeyScopedAgentSessionStore</c> class.
    /// </para>
    /// <para>
    /// If nothing is registered, the default bridge built on
    /// <see cref="AgentSessionManager"/> is used. This keeps
    /// <c>MapAgentPrism()</c> as the single entry point and requires no extra
    /// registration step (the no-surprises rule).
    /// </para>
    /// </remarks>
    private static AgentSessionStore ResolveSessionStore(IServiceProvider services)
        => services.GetService<AgentSessionStore>()
           ?? new AgentPrismAgentSessionStore(services.GetRequiredService<AgentSessionManager>());
}
