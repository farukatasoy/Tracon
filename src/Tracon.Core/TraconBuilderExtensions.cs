using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// The registration methods of the Tracon configuration chain that
/// <c>AddTracon()</c> returns.
/// </summary>
/// <remarks>
/// <para>
/// Every registration capability is an extension method on
/// <see cref="ITraconBuilder"/>, so a new capability never changes the
/// interface. Provider and storage packages follow the same shape with their
/// own extensions (<c>UsePostgreSql()</c>, <c>UseOpenAI()</c>, and so on).
/// </para>
/// <para>
/// A chain only has to supply <see cref="ITraconBuilder.Services"/>: these
/// methods write to that collection and nothing else.
/// </para>
/// </remarks>
public static partial class TraconBuilderExtensions
{
    /// <summary>Modifies the runtime settings.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">The settings modifier.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Runs after the configuration section is bound, so a value set here wins
    /// over <c>appsettings.json</c>.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .Configure(options => options.Tools.DefaultTimeout = TimeSpan.FromSeconds(60));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder Configure(this ITraconBuilder builder, Action<TraconOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);
        return builder;
    }

    /// <summary>
    /// Declares that <typeparamref name="T"/> must resolve to the application's
    /// own registration. The host does not start when Tracon's built-in
    /// default is what resolves.
    /// </summary>
    /// <typeparam name="T">
    /// One of the seven embedding points: <see cref="ITenantContext"/>,
    /// <see cref="IRunAttributionContext"/>, <see cref="IToolAuthorizationHandler"/>,
    /// <see cref="IRunAuthorizationHandler"/>, <see cref="IRunEventSink"/>,
    /// <see cref="IAttachmentStorage"/>, or <see cref="IToolApprovalPresenter"/>.
    /// Any other type stops the host from starting, with a message naming the seven.
    /// </typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Off by default: an application that never calls this behaves exactly as
    /// before. Every extension point is registered with <c>TryAdd</c>, so a host
    /// that binds nothing runs on the built-in default and starts silently. That
    /// suits a first run; it does not suit a deployment whose module order can
    /// leave an authorization handler on the permissive default without anyone
    /// noticing until the first unauthorized request.
    /// </para>
    /// <para>
    /// The check runs while the host starts, not when endpoints are mapped, so
    /// an embedded host with no HTTP surface gets the same guarantee. Register
    /// the implementation BEFORE <c>AddTracon()</c>: a <c>TryAdd</c>
    /// registration made afterwards is dropped, and the built-in default stays.
    /// </para>
    /// <para>
    /// This is a composition gate. It proves which implementation is bound; it
    /// proves nothing about whether that implementation decides correctly.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Services.AddSingleton&lt;IRunAuthorizationHandler, OrderDeskAuthorization&gt;();
    ///
    /// builder.AddTracon()
    ///        .RequireCustomBinding&lt;IRunAuthorizationHandler&gt;()
    ///        .RequireCustomBinding&lt;IToolAuthorizationHandler&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder RequireCustomBinding<T>(this ITraconBuilder builder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The type argument is NOT checked here. Whether the container ends up
        // on the built-in default is only knowable once every module has
        // registered, so the answer belongs to host start, and reporting a
        // wrong type argument from the same place keeps one error surface
        // instead of two.
        builder.Services.AddSingleton(new RequiredBindingRegistration(typeof(T)));
        return builder;
    }

    /// <summary>
    /// Refuses to start the host while a security-sensitive decision is still
    /// on its permissive default and the deployment has not accepted the risk
    /// by name.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">
    /// The risks this deployment has deliberately decided to carry. Omit it to
    /// accept none, which is the strictest form.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>This does not make anything secure and changes no setting.</strong>
    /// It sets no value, chooses no policy and turns nothing on. The only thing
    /// it does is turn a skipped decision into a startup failure - which is the
    /// one thing a permissive default cannot do for itself.
    /// </para>
    /// <para>
    /// Off by default: an application that never calls this behaves exactly as
    /// before. <c>AddTracon()</c> brings every security-sensitive switch up
    /// permissive on purpose, so a first run surprises nobody. A production
    /// deployment wants the opposite, and today it can reach production having
    /// never separated tenants, never decided who owns a session and never
    /// registered a content guard, in complete silence.
    /// </para>
    /// <para>
    /// Six decisions are asked about: tenant separation, session ownership,
    /// at-rest content protection, content inspection, request rate limiting
    /// and retention. Each one is either answered by turning the feature on, or
    /// accepted by name. Acceptance is per item and there is no way to accept
    /// them all at once.
    /// </para>
    /// <para>
    /// <strong>The set of decisions is a versioned contract.</strong> A later
    /// release that adds one stops a host that calls this method until the new
    /// decision is answered or accepted. Read that cost before adopting the
    /// method: it is the method working, not failing.
    /// </para>
    /// <para>
    /// This is a composition gate, not a security proof. It proves a feature is
    /// switched on; it proves nothing about whether the policy behind it is
    /// right.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .RequireProductionProfile(profile => profile
    ///            .Accept(TraconProductionRisk.SingleTenant)
    ///            .Accept(TraconProductionRisk.UnboundedRetention));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder RequireProductionProfile(this ITraconBuilder builder, Action<TraconProductionProfileOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new TraconProductionProfileOptions();
        configure?.Invoke(options);

        // Add, not TryAdd: TryAddEnumerable deduplicates by implementation TYPE
        // and every declaration shares one, so two composition modules that both
        // declare the profile would collapse into whichever ran first. The
        // validator takes the union of the accepts, so a second declaration is
        // a no-op rather than a conflict.
        builder.Services.AddSingleton(new ProductionProfileRegistration(options));
        return builder;
    }
}
