using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// The fluent chain that configures Tracon. Returned from the
/// <c>AddTracon()</c> call.
/// </summary>
/// <remarks>
/// Provider and storage packages add their own extensions to this chain:
/// <c>UsePostgreSql()</c>, <c>UseOpenAI()</c>, and so on.
/// </remarks>
public interface ITraconBuilder
{
    /// <summary>The underlying service collection.</summary>
    /// <remarks>
    /// <para>
    /// The escape hatch: anything Tracon does not model is registered here.
    /// Every Tracon service is registered with <c>TryAdd</c>, so registration
    /// ORDER decides who wins, and the two seam shapes behave differently.
    /// </para>
    /// <para>
    /// For a single-instance seam (for example <see cref="ITenantContext"/>): a
    /// registration made before <c>AddTracon()</c> wins outright, and only
    /// one registration remains. A registration made after <c>AddTracon()</c>
    /// also wins for a direct resolve, but Tracon's own registration is not
    /// removed - it stays behind as a second, unused entry.
    /// </para>
    /// <para>
    /// For a multi-registration seam (for example <see cref="IAgentDecorator"/>):
    /// a registration made before <c>AddTracon()</c> joins the list alongside
    /// the built-in ones. A registration made after <c>AddTracon()</c> also
    /// joins the list - the built-in implementation keeps running too, which is a
    /// real behavior difference from the single-instance case above.
    /// </para>
    /// <example>
    /// <code>
    /// // Runs before AddTracon(), so this registration wins outright.
    /// builder.Services.AddSingleton(new OrderGateway());
    /// builder.AddTracon();
    /// </code>
    /// </example>
    /// </remarks>
    IServiceCollection Services { get; }

    /// <summary>Modifies the runtime settings.</summary>
    /// <param name="configure">The settings modifier.</param>
    /// <returns>The chain, for further configuration.</returns>
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
    ITraconBuilder Configure(Action<TraconOptions> configure);

    /// <summary>Registers a tool.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The AOT-safe overload: the caller supplies the built
    /// <see cref="AIFunction"/>, so no reflection is involved.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddTool(refundTool, options => options.RequiresApproval = true);
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddTool(AIFunction tool, Action<ToolRegistrationOptions> configure);

    /// <summary>Registers a tool with default metadata.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddTool(AIFunction tool);

    /// <summary>Registers a tool that runs inside its own dependency-injection scope on every call.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// <para>
    /// Identical in shape to <see cref="AddTool(AIFunction, Action{ToolRegistrationOptions})"/> —
    /// the one difference is the word "scoped", and its meaning is exactly
    /// this: every call opens a fresh <see cref="IServiceScope"/>, exposes it
    /// through <see cref="AIFunctionArguments.Services"/>, and closes it as
    /// soon as the call finishes. Microsoft Agent Framework otherwise passes
    /// an empty provider there — a dependency resolved from
    /// <c>AIFunctionArguments.Services</c> without this method throws or
    /// returns nothing, it is never silently wrong.
    /// </para>
    /// <para>
    /// Use this for a tool that needs a repository, a <c>DbContext</c>, or
    /// any other per-call dependency. Two concurrent calls never share a
    /// scope.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddScopedTool(AIFunctionFactory.Create(
    ///            async (string orderId, AIFunctionArguments arguments) =>
    ///            {
    ///                var orders = arguments.Services!.GetRequiredService&lt;IOrderRepository&gt;();
    ///                return await orders.GetAsync(orderId);
    ///            },
    ///            "get_order"),
    ///            options => options.RequiredPermission = "orders.read");
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddScopedTool(AIFunction tool, Action<ToolRegistrationOptions> configure);

    /// <summary>Registers a tool that runs inside its own dependency-injection scope on every call, with default metadata.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddScopedTool(AIFunction tool);

    /// <summary>Builds and registers a tool from a method.</summary>
    /// <param name="method">The method to expose as a tool.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    [RequiresUnreferencedCode("Building a tool from a method uses reflection; type information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Building a tool from a method may require code generation at runtime.")]
    ITraconBuilder AddTool(
        Delegate method,
        string? name = null,
        string? description = null,
        Action<ToolRegistrationOptions>? configure = null);

    /// <summary>
    /// Registers, as tools, the methods on a type that are marked with
    /// <see cref="TraconToolAttribute"/>.
    /// </summary>
    /// <typeparam name="T">The type to scan.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="TraconException">
    /// <typeparamref name="T"/> has no marked method, or a marked method cannot
    /// be converted to a tool.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Marking is an explicit choice: every new method added to the class is
    /// not automatically exposed to agents. Static methods bind directly; for
    /// instance methods, the owning object is resolved from the service
    /// provider at call time.
    /// </para>
    /// <para>
    /// <strong>A static class cannot be a type argument</strong> (a C# rule).
    /// If your tools live in a <c>static class</c>, use the
    /// <see cref="AddToolsFrom(Type)"/> overload instead.
    /// </para>
    /// <para>
    /// This method uses reflection and is not safe under trimming or native AOT
    /// scenarios. Applications targeting AOT should use the
    /// <see cref="AddTool(AIFunction, Action{ToolRegistrationOptions})"/> overload instead.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddToolsFrom&lt;OrderTools&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Tool scanning may require code generation at runtime.")]
    ITraconBuilder AddToolsFrom<T>();

    /// <summary>
    /// Registers, as tools, the methods on a type that are marked with
    /// <see cref="TraconToolAttribute"/>.
    /// </summary>
    /// <param name="type">The type to scan. May be a <c>static class</c>.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// <paramref name="type"/> has no marked method, or a marked method cannot
    /// be converted to a tool.
    /// </exception>
    /// <remarks>
    /// A static class cannot be a type argument under C# rules; this overload
    /// makes the <c>AddToolsFrom(typeof(OrderTools))</c> form possible.
    /// </remarks>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Tool scanning may require code generation at runtime.")]
    ITraconBuilder AddToolsFrom(Type type);

    /// <summary>
    /// Defines a declarative agent in code. The definition passes through the
    /// Tracon compiler; model and tool validation is applied.
    /// </summary>
    /// <param name="definition">The agent definition.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgent(new AgentDefinition
    ///        {
    ///            Name = "support",
    ///            Instructions = "Answer support questions from the order data.",
    ///            Model = new ModelBinding { Provider = "openai", Model = "gpt-4o-mini" },
    ///            ToolNames = ["get_order_status"],
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddAgent(AgentDefinition definition);

    /// <summary>
    /// Defines a skill in code. A skill defined in code takes precedence over a
    /// runtime skill with the same name.
    /// </summary>
    /// <param name="skill">The skill to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// <para>
    /// A skill is instruction text an agent loads by name. When it carries
    /// <see cref="AgentSkillDefinition.Scripts"/>, those scripts run on the
    /// server like stored ones: they need <c>AllowStoredScripts</c>, an
    /// allow-listed interpreter, and a grant pinned to their content. The hash
    /// to grant is read from <c>GET /api/skills/{name}</c>, which resolves a code
    /// skill first. A deployment that changes a script changes its hash, so the
    /// script needs a new grant before it runs again.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddSkill(new AgentSkillDefinition
    ///        {
    ///            TenantId = "default",
    ///            Name = "refund-policy",
    ///            Description = "How a refund decision is made.",
    ///            Instructions = "A refund under 100 USD is approved without review.",
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddSkill(AgentSkillDefinition skill);

    /// <summary>
    /// Defines a factory-based agent in code. How the agent is built is
    /// entirely up to the caller.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="factory">The factory that produces the agent.</param>
    /// <param name="description">A short description.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddAgent(string name, Func<IServiceProvider, AIAgent> factory, string? description = null);

    /// <summary>Registers a custom agent source as a singleton.</summary>
    /// <typeparam name="TSource">The source implementation type.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Calling this method more than once for the same source type has no effect.
    /// The source can serve global or tenant-aware agents. It must be thread-safe
    /// because the catalog calls its methods concurrently.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentSource&lt;GitAgentSource&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddAgentSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>() where TSource : class, IAgentSource;

    /// <summary>Registers a configured custom agent source as a singleton.</summary>
    /// <param name="source">The source instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddAgentSource(IAgentSource source);

    /// <summary>Registers a custom agent-source factory as a singleton.</summary>
    /// <param name="factory">The factory that creates the source.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddAgentSource(Func<IServiceProvider, IAgentSource> factory);

    /// <summary>Registers a custom agent decorator as a singleton.</summary>
    /// <typeparam name="TDecorator">The decorator implementation type.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Calling this method more than once for the same decorator type has no
    /// effect. The decorator joins the pipeline alongside Tracon's own
    /// (run recording, telemetry, tool approval); see
    /// <see cref="IAgentDecorator.Order"/> for where it lands. It must be
    /// thread-safe because the catalog can decorate agents concurrently.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddAgentDecorator&lt;AuditingAgentDecorator&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddAgentDecorator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDecorator>() where TDecorator : class, IAgentDecorator;

    /// <summary>Registers a configured custom agent decorator as a singleton.</summary>
    /// <param name="decorator">The decorator instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddAgentDecorator(IAgentDecorator decorator);

    /// <summary>Registers a custom agent-decorator factory as a singleton.</summary>
    /// <param name="factory">The factory that creates the decorator.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddAgentDecorator(Func<IServiceProvider, IAgentDecorator> factory);

    /// <summary>Registers a custom run judge as a singleton.</summary>
    /// <typeparam name="TJudge">The judge implementation type.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Repeating this overload for the same implementation type has no effect.
    /// The container creates and owns the singleton. The judge must be
    /// thread-safe because evaluations can overlap, including when a timed-out
    /// call finishes after a retry starts.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddRunJudge&lt;ResponseQualityJudge&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddRunJudge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJudge>() where TJudge : class, IRunJudge;

    /// <summary>Registers a configured run-judge instance as a singleton.</summary>
    /// <param name="judge">The judge instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The caller owns the instance and any resources it holds. Different
    /// configured instances of the same CLR type are preserved. Their
    /// <see cref="IRunJudge.Name"/> values must still be unique.
    /// </remarks>
    ITraconBuilder AddRunJudge(IRunJudge judge);

    /// <summary>Registers a run-judge factory as a singleton.</summary>
    /// <param name="factory">The factory that creates the judge.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The container owns the produced singleton. The factory must not capture
    /// a scoped dependency because the result outlives that scope. Different
    /// factories and configured results are preserved.
    /// </remarks>
    ITraconBuilder AddRunJudge(Func<IServiceProvider, IRunJudge> factory);

    /// <summary>Registers a custom model provider as a singleton.</summary>
    /// <typeparam name="TProvider">The provider implementation type.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Calling this method more than once for the same provider type has no
    /// effect. Prefer this overload when the provider has no state to
    /// configure by hand; use <see cref="AddModelProvider(IModelProvider)"/>
    /// or the factory overload when it does.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddModelProvider&lt;OnPremiseModelProvider&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddModelProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>() where TProvider : class, IModelProvider;

    /// <summary>Registers a model provider.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The shipped provider packages (<c>UseOpenAI()</c>, <c>UseAnthropic()</c>,
    /// and the rest) call this method. Register your own provider here when the
    /// model sits behind an endpoint none of them describes.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddModelProvider(new OnPremiseModelProvider(endpoint));
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddModelProvider(IModelProvider provider);

    /// <summary>Registers a model provider through a factory.</summary>
    /// <param name="factory">The factory that produces the provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    ITraconBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory);

    /// <summary>
    /// Registers a custom eval check. Eval suites can reference it by
    /// this <paramref name="kind"/> name in their <c>checks</c> field.
    /// </summary>
    /// <param name="kind">The check type name. Must not collide with a built-in type (for example <c>nonEmpty</c>).</param>
    /// <param name="check">A code-written check that does not call the model.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// An <c>EvalCheck</c> produced with
    /// <c>Microsoft.Agents.AI.FunctionEvaluator.Create(...)</c> is expected.
    /// Same rationale as tools: custom logic is only defined in code, and a
    /// free-form expression cannot be written from the interface.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddEvalCheck("mentionsOrderId", FunctionEvaluator.Create(
    ///            "mentionsOrderId",
    ///            response => response.Contains("order", StringComparison.OrdinalIgnoreCase)));
    /// </code>
    /// </example>
    /// </remarks>
    ITraconBuilder AddEvalCheck(string kind, Microsoft.Agents.AI.EvalCheck check);

    /// <summary>
    /// Registers a code-defined loop stop criterion. A definition's
    /// <see cref="LoopSettings.Criteria"/> can then reference it by this
    /// <paramref name="kind"/> name.
    /// </summary>
    /// <param name="kind">
    /// The criterion name. It must not be one of the built-in kinds
    /// (<c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>,
    /// <c>backgroundTaskCompletion</c>); shadowing one is rejected, so the same
    /// definition cannot mean different things in two applications.
    /// </param>
    /// <param name="evaluator">
    /// The criterion. <c>Microsoft.Agents.AI.DelegateLoopEvaluator</c> wraps a
    /// plain function; a subclass of <c>LoopEvaluator</c> covers anything larger.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Same rationale as tools and eval checks: a stop criterion whose logic is
    /// code is only ever defined in code. A definition arriving from the
    /// management API names the criterion, it never writes it.
    /// <example>
    /// The loop types are marked for evaluation only by Microsoft Agent
    /// Framework, so naming one in your own code needs the suppression below:
    /// <code>
    /// #pragma warning disable MAAI001
    /// builder.AddTracon()
    ///        .AddLoopEvaluator("hasCitations", new DelegateLoopEvaluator((context, ct) =>
    ///            new ValueTask&lt;LoopEvaluation&gt;(
    ///                context.LastResponse?.Text?.Contains("[1]", StringComparison.Ordinal) == true
    ///                    ? LoopEvaluation.Stop()
    ///                    : LoopEvaluation.Continue("Add a numbered citation for every claim."))));
    /// #pragma warning restore MAAI001
    /// </code>
    /// </example>
    /// </remarks>
    // MAAI001: Microsoft.Agents.AI.LoopEvaluator is marked "evaluation purposes
    // only". A registration surface cannot hide the type it registers, so the
    // suppression is narrowed to this one member instead of the file. Rationale:
    // docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001
    ITraconBuilder AddLoopEvaluator(string kind, Microsoft.Agents.AI.LoopEvaluator evaluator);
#pragma warning restore MAAI001

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
    /// <returns>The chain, for further configuration.</returns>
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
    ITraconBuilder RequireCustomBinding<T>()
        where T : class;

    /// <summary>
    /// Refuses to start the host while a security-sensitive decision is still
    /// on its permissive default and the deployment has not accepted the risk
    /// by name.
    /// </summary>
    /// <param name="configure">
    /// The risks this deployment has deliberately decided to carry. Omit it to
    /// accept none, which is the strictest form.
    /// </param>
    /// <returns>The chain, for further configuration.</returns>
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
    ITraconBuilder RequireProductionProfile(Action<TraconProductionProfileOptions>? configure = null);
}
