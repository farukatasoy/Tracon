namespace Tracon;

/// <summary>
/// Access and behavior settings for the endpoints connected via <c>MapTracon</c>.
/// </summary>
/// <remarks>
/// <para>
/// Access protection has three layers, applied in this order:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <strong>Loopback restriction</strong> — while <see cref="AllowRemoteAccess"/>
/// is off (default), a request from outside loopback gets <c>403</c>. Protects
/// against accidental exposure to the outside.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Bearer token</strong> — when <see cref="AuthToken"/> is set, the
/// <c>Authorization: Bearer</c> header is checked with a constant-time comparison.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Authorization policy</strong> — <see cref="RequireAuthorization(string)"/>
/// connects to the ASP.NET Core authentication pipeline. This is the path used
/// in production.
/// </description>
/// </item>
/// </list>
/// <para>
/// This type is deliberately <strong>not</strong> a <c>record</c>. A <c>record</c>'s
/// compiler-generated <c>ToString</c> writes every property and a single log
/// line would leak the <see cref="AuthToken"/> value.
/// </para>
/// </remarks>
public sealed class TraconEndpointOptions
{
    /// <summary>
    /// Whether requests from outside loopback are allowed. Default
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Reverse proxy warning.</strong> If the application sits behind a
    /// reverse proxy, the connection's remote address is the proxy's address,
    /// which is usually loopback. In that case the loopback restriction
    /// protects nothing. Behind a reverse proxy, configure the
    /// <c>ForwardedHeaders</c> middleware and base protection on
    /// <see cref="AuthToken"/> or <see cref="RequireAuthorization(string)"/>.
    /// </para>
    /// </remarks>
    public bool AllowRemoteAccess { get; set; }

    /// <summary>
    /// Expected bearer token. If left empty, no token check is performed.
    /// </summary>
    /// <remarks>
    /// This is a <strong>secret</strong>. Do not write it to a configuration
    /// file; use <c>dotnet user-secrets</c> or an environment variable. The
    /// value never appears in any response, in <c>/api/meta</c> output, or in
    /// a log line.
    /// </remarks>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Name of the ASP.NET Core authorization policy applied to the endpoints.
    /// Set via <see cref="RequireAuthorization(string)"/>.
    /// </summary>
    public string? AuthorizationPolicy { get; private set; }

    /// <summary>
    /// Interval between polls while streaming events for a running run.
    /// Default 250 ms.
    /// </summary>
    /// <remarks>
    /// The event store offers no notification channel; live streaming is
    /// achieved by polling the store for new events. A smaller value reduces
    /// latency but increases database load.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan RunEventPollInterval
    {
        get;

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Attaches the endpoints to an ASP.NET Core authorization policy.
    /// </summary>
    /// <param name="policyName">Name of the policy to apply.</param>
    /// <exception cref="ArgumentException"><paramref name="policyName"/> is empty.</exception>
    /// <remarks>
    /// The policy applies to every endpoint except <c>{prefix}/api/meta</c>. The
    /// meta endpoint stays open so the UI can learn which authentication method
    /// to use, and it returns no sensitive data.
    /// </remarks>
    public void RequireAuthorization(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        AuthorizationPolicy = policyName;
    }

    /// <summary>
    /// Requires that the endpoint role policies (<see cref="TraconPolicies"/>)
    /// be registered in the consumer's authorization configuration. Default
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>When off</strong> (default), an unregistered role policy is
    /// silently skipped; the corresponding endpoint only passes through the
    /// existing three-layer protection. This exists so a version upgrade does
    /// not break existing setups.
    /// </para>
    /// <para>
    /// <strong>When on</strong>, <c>MapTracon()</c> fails at startup if any
    /// of the <c>Tracon.Reader</c>, <c>Tracon.Operator</c>,
    /// <c>Tracon.Admin</c> policies is not defined via <c>AddAuthorization</c>.
    /// A production setup should turn this on; a door left silently open is
    /// worse than a door believed to be closed.
    /// </para>
    /// <para>
    /// A policy provider that THROWS while a role name is resolved is treated
    /// as "not registered" in both modes. It is written to the log as a
    /// warning (category <c>Tracon.RolePolicies</c>); when this option is on,
    /// the startup exception carries the provider's exception as its inner
    /// exception.
    /// </para>
    /// </remarks>
    public bool RequireRolePolicies { get; set; }

    /// <summary>
    /// Whether the <c>GET /api/diagnostics</c> endpoint is connected. Default
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A diagnostics endpoint reveals information about the setup (persistence
    /// provider, migration status, which configuration keys are resolved) even
    /// though it carries no <c>secret</c> value. Per the no-surprises rule it must be explicitly
    /// enabled; default off.
    /// </para>
    /// <para>
    /// When enabled the endpoint requires the <see cref="TraconPolicies.Admin"/>
    /// role. If the role policy is not registered, the endpoint still passes
    /// through the three-layer protection (loopback + bearer token).
    /// </para>
    /// </remarks>
    public bool EnableDiagnosticsEndpoint { get; set; }

    /// <summary>
    /// Whether the OpenAI-compatible <c>/v1/conversations</c> endpoints are
    /// connected. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These four endpoints exist so an OpenAI client library can list, read
    /// and delete a conversation by the same identifier it passes to
    /// <c>/v1/responses</c>. A deployment that drives Tracon only through
    /// its own <c>/api/sessions</c> routes never calls them, and turning them
    /// off removes the routes entirely: the paths answer <c>404</c> and vanish
    /// from the OpenAPI document.
    /// </para>
    /// <para>
    /// Default <see langword="true"/> because the surface has shipped and
    /// silently withdrawing it would break existing clients. It is a switch for
    /// a deployment that wants a smaller attack surface, not a security
    /// control: the endpoints go through the same role policies, API key
    /// scopes, session ownership and <see cref="IRunAuthorizationHandler"/>
    /// gate as every other session route, and leaving them mapped opens no door
    /// that <c>/api/sessions</c> does not open already.
    /// </para>
    /// <para>
    /// The other OpenAI-compatible surfaces — <c>/v1/responses</c> and
    /// <c>/v1/chat/completions</c> — are NOT governed by this flag. They start
    /// runs rather than reach a stored conversation, and a deployment that
    /// wants them gone should not have to give up the conversation routes to
    /// say so.
    /// </para>
    /// </remarks>
    public bool MapOpenAIConversations { get; set; } = true;

    /// <summary>
    /// Origins allowed to call the Tracon endpoints from a browser. Empty
    /// by default — no <c>Access-Control-Allow-Origin</c> header is ever
    /// sent, so a cross-origin browser request is blocked by the browser
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no <c>AllowAnyOrigin</c> option: an endpoint that carries a
    /// bearer token or an API key must not make a wildcard origin easy to
    /// reach for. Add exact origins, for example
    /// <c>options.AllowedOrigins.Add("https://shop.example.com")</c>.
    /// </para>
    /// <para>
    /// This exists for the embeddable chat component: a
    /// consumer's own page, served from its own origin, calls
    /// <c>{prefix}/api/agents/{name}/run</c> directly from the browser.
    /// </para>
    /// <para>
    /// An allowed origin can read a response from <strong>any</strong>
    /// endpoint under <c>{prefix}</c>, not only the run endpoint — CORS is
    /// applied to the whole group, not path by path. This is deliberately
    /// broad rather than a source of extra privilege WHEN a credential layer
    /// is configured: reading a response then requires a valid bearer token or
    /// a correctly scoped API key, and CORS only controls whether the browser
    /// lets the page's own script read what that credential already permits.
    /// <strong>With no credential layer configured</strong> - no
    /// <see cref="AuthToken"/> and no <see cref="AuthorizationPolicy"/>, which
    /// is the default - an allowed origin needs no credential at all, and every
    /// script on that origin can read every endpoint under the prefix. Add an
    /// origin only where you also control which credential reaches it, and do
    /// not add one before the credential layer is on.
    /// </para>
    /// </remarks>
    public IList<string> AllowedOrigins { get; } = [];
}
