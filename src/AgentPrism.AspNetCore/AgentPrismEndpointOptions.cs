namespace AgentPrism;

/// <summary>
/// Access and behavior settings for the endpoints connected via <c>MapAgentPrism</c>.
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
/// Rationale: <c>docs/KARARLAR.md</c>, decision K-035.
/// </para>
/// </remarks>
public sealed class AgentPrismEndpointOptions
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
    /// Requires that the endpoint role policies (<see cref="AgentPrismPolicies"/>)
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
    /// <strong>When on</strong>, <c>MapAgentPrism()</c> fails at startup if any
    /// of the <c>AgentPrism.Reader</c>, <c>AgentPrism.Operator</c>,
    /// <c>AgentPrism.Admin</c> policies is not defined via <c>AddAuthorization</c>.
    /// A production setup should turn this on; a door left silently open is
    /// worse than a door believed to be closed.
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
    /// though it carries no <c>secret</c> value. Per K1 it must be explicitly
    /// enabled; default off.
    /// </para>
    /// <para>
    /// When enabled the endpoint requires the <see cref="AgentPrismPolicies.Admin"/>
    /// role. If the role policy is not registered, the endpoint still passes
    /// through the three-layer protection (loopback + bearer token).
    /// </para>
    /// </remarks>
    public bool EnableDiagnosticsEndpoint { get; set; }

    /// <summary>
    /// Origins allowed to call the AgentPrism endpoints from a browser. Empty
    /// by default — no <c>Access-Control-Allow-Origin</c> header is ever
    /// sent, so a cross-origin browser request is blocked by the browser
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no <c>AllowAnyOrigin</c> option: an endpoint that carries a
    /// bearer token or an API key must not make a wildcard origin easy to
    /// reach for (K1). Add exact origins, for example
    /// <c>options.AllowedOrigins.Add("https://shop.example.com")</c>.
    /// </para>
    /// <para>
    /// This exists for the embeddable chat component (Phase 61): a
    /// consumer's own page, served from its own origin, calls
    /// <c>{prefix}/api/agents/{name}/run</c> directly from the browser.
    /// </para>
    /// <para>
    /// 🚨 An allowed origin can read a response from <strong>any</strong>
    /// endpoint under <c>{prefix}</c>, not only the run endpoint — CORS is
    /// applied to the whole group, not path by path. This is deliberately
    /// broad rather than a source of extra privilege: reading a response
    /// still requires a valid credential (bearer token or a correctly scoped
    /// API key), and CORS only controls whether the browser lets the page's
    /// own script read what that credential already permits. Add an origin
    /// only where you also control which credential reaches that origin.
    /// </para>
    /// </remarks>
    public IList<string> AllowedOrigins { get; } = [];
}
