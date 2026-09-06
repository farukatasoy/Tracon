using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// The fluent chain that configures AgentPrism. Returned from the
/// <c>AddAgentPrism()</c> call.
/// </summary>
/// <remarks>
/// Provider and storage packages add their own extensions to this chain:
/// <c>UsePostgreSql()</c>, <c>UseOpenAI()</c>, and so on.
/// </remarks>
public interface IAgentPrismBuilder
{
    /// <summary>The underlying service collection.</summary>
    /// <remarks>
    /// <para>
    /// The escape hatch: anything AgentPrism does not model is registered here.
    /// Every AgentPrism service is registered with <c>TryAdd</c>, so registration
    /// ORDER decides who wins, and the two seam shapes behave differently.
    /// </para>
    /// <para>
    /// For a single-instance seam (for example <see cref="ITenantContext"/>): a
    /// registration made before <c>AddAgentPrism()</c> wins outright, and only
    /// one registration remains. A registration made after <c>AddAgentPrism()</c>
    /// also wins for a direct resolve, but AgentPrism's own registration is not
    /// removed - it stays behind as a second, unused entry.
    /// </para>
    /// <para>
    /// For a multi-registration seam (for example <see cref="IAgentDecorator"/>):
    /// a registration made before <c>AddAgentPrism()</c> joins the list alongside
    /// the built-in ones. A registration made after <c>AddAgentPrism()</c> also
    /// joins the list - the built-in implementation keeps running too, which is a
    /// real behavior difference from the single-instance case above.
    /// </para>
    /// <example>
    /// <code>
    /// // Runs before AddAgentPrism(), so this registration wins outright.
    /// builder.Services.AddSingleton(new OrderGateway());
    /// builder.AddAgentPrism();
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
    /// builder.AddAgentPrism()
    ///        .Configure(options => options.Tools.DefaultTimeout = TimeSpan.FromSeconds(60));
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder Configure(Action<AgentPrismOptions> configure);

    /// <summary>Registers a tool.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The AOT-safe overload: the caller supplies the built
    /// <see cref="AIFunction"/>, so no reflection is involved.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddTool(refundTool, options => options.RequiresApproval = true);
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddTool(AIFunction tool, Action<ToolRegistrationOptions> configure);

    /// <summary>Registers a tool with default metadata.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddTool(AIFunction tool);

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
    /// builder.AddAgentPrism()
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
    IAgentPrismBuilder AddScopedTool(AIFunction tool, Action<ToolRegistrationOptions> configure);

    /// <summary>Registers a tool that runs inside its own dependency-injection scope on every call, with default metadata.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddScopedTool(AIFunction tool);

    /// <summary>Builds and registers a tool from a method.</summary>
    /// <param name="method">The method to expose as a tool.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The tool description.</param>
    /// <param name="configure">Configures the tool metadata.</param>
    /// <returns>The chain, for further configuration.</returns>
    [RequiresUnreferencedCode("Building a tool from a method uses reflection; type information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Building a tool from a method may require code generation at runtime.")]
    IAgentPrismBuilder AddTool(
        Delegate method,
        string? name = null,
        string? description = null,
        Action<ToolRegistrationOptions>? configure = null);

    /// <summary>
    /// Registers, as tools, the methods on a type that are marked with
    /// <see cref="AgentPrismToolAttribute"/>.
    /// </summary>
    /// <typeparam name="T">The type to scan.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="AgentPrismException">
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
    /// builder.AddAgentPrism()
    ///        .AddToolsFrom&lt;OrderTools&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Tool scanning may require code generation at runtime.")]
    IAgentPrismBuilder AddToolsFrom<T>();

    /// <summary>
    /// Registers, as tools, the methods on a type that are marked with
    /// <see cref="AgentPrismToolAttribute"/>.
    /// </summary>
    /// <param name="type">The type to scan. May be a <c>static class</c>.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// <paramref name="type"/> has no marked method, or a marked method cannot
    /// be converted to a tool.
    /// </exception>
    /// <remarks>
    /// A static class cannot be a type argument under C# rules; this overload
    /// makes the <c>AddToolsFrom(typeof(OrderTools))</c> form possible.
    /// </remarks>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Tool scanning may require code generation at runtime.")]
    IAgentPrismBuilder AddToolsFrom(Type type);

    /// <summary>
    /// Defines a declarative agent in code. The definition passes through the
    /// AgentPrism compiler; model and tool validation is applied.
    /// </summary>
    /// <param name="definition">The agent definition.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
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
    IAgentPrismBuilder AddAgent(AgentDefinition definition);

    /// <summary>
    /// Defines a skill in code. A skill defined in code takes precedence over a
    /// runtime skill with the same name.
    /// </summary>
    /// <param name="skill">The skill to register.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// A skill is instruction text an agent loads by name; it carries no code.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
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
    IAgentPrismBuilder AddSkill(AgentSkillDefinition skill);

    /// <summary>
    /// Defines a factory-based agent in code. How the agent is built is
    /// entirely up to the caller.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <param name="factory">The factory that produces the agent.</param>
    /// <param name="description">A short description.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddAgent(string name, Func<IServiceProvider, AIAgent> factory, string? description = null);

    /// <summary>Registers a custom agent source as a singleton.</summary>
    /// <typeparam name="TSource">The source implementation type.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Calling this method more than once for the same source type has no effect.
    /// The source can serve global or tenant-aware agents. It must be thread-safe
    /// because the catalog calls its methods concurrently.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddAgentSource&lt;GitAgentSource&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddAgentSource<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSource>() where TSource : class, IAgentSource;

    /// <summary>Registers a configured custom agent source as a singleton.</summary>
    /// <param name="source">The source instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddAgentSource(IAgentSource source);

    /// <summary>Registers a custom agent-source factory as a singleton.</summary>
    /// <param name="factory">The factory that creates the source.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddAgentSource(Func<IServiceProvider, IAgentSource> factory);

    /// <summary>Registers a custom agent decorator as a singleton.</summary>
    /// <typeparam name="TDecorator">The decorator implementation type.</typeparam>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// Calling this method more than once for the same decorator type has no
    /// effect. The decorator joins the pipeline alongside AgentPrism's own
    /// (run recording, telemetry, tool approval); see
    /// <see cref="IAgentDecorator.Order"/> for where it lands. It must be
    /// thread-safe because the catalog can decorate agents concurrently.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddAgentDecorator&lt;AuditingAgentDecorator&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddAgentDecorator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDecorator>() where TDecorator : class, IAgentDecorator;

    /// <summary>Registers a configured custom agent decorator as a singleton.</summary>
    /// <param name="decorator">The decorator instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddAgentDecorator(IAgentDecorator decorator);

    /// <summary>Registers a custom agent-decorator factory as a singleton.</summary>
    /// <param name="factory">The factory that creates the decorator.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddAgentDecorator(Func<IServiceProvider, IAgentDecorator> factory);

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
    /// builder.AddAgentPrism()
    ///        .AddRunJudge&lt;ResponseQualityJudge&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddRunJudge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJudge>() where TJudge : class, IRunJudge;

    /// <summary>Registers a configured run-judge instance as a singleton.</summary>
    /// <param name="judge">The judge instance.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The caller owns the instance and any resources it holds. Different
    /// configured instances of the same CLR type are preserved. Their
    /// <see cref="IRunJudge.Name"/> values must still be unique.
    /// </remarks>
    IAgentPrismBuilder AddRunJudge(IRunJudge judge);

    /// <summary>Registers a run-judge factory as a singleton.</summary>
    /// <param name="factory">The factory that creates the judge.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The container owns the produced singleton. The factory must not capture
    /// a scoped dependency because the result outlives that scope. Different
    /// factories and configured results are preserved.
    /// </remarks>
    IAgentPrismBuilder AddRunJudge(Func<IServiceProvider, IRunJudge> factory);

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
    /// builder.AddAgentPrism()
    ///        .AddModelProvider&lt;OnPremiseModelProvider&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddModelProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>() where TProvider : class, IModelProvider;

    /// <summary>Registers a model provider.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The shipped provider packages (<c>UseOpenAI()</c>, <c>UseAnthropic()</c>,
    /// and the rest) call this method. Register your own provider here when the
    /// model sits behind an endpoint none of them describes.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddModelProvider(new OnPremiseModelProvider(endpoint));
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddModelProvider(IModelProvider provider);

    /// <summary>Registers a model provider through a factory.</summary>
    /// <param name="factory">The factory that produces the provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory);

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
    /// builder.AddAgentPrism()
    ///        .AddEvalCheck("mentionsOrderId", FunctionEvaluator.Create(
    ///            "mentionsOrderId",
    ///            response => response.Contains("order", StringComparison.OrdinalIgnoreCase)));
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddEvalCheck(string kind, Microsoft.Agents.AI.EvalCheck check);

    /// <summary>
    /// Declares that <typeparamref name="T"/> must resolve to the application's
    /// own registration. The host does not start when AgentPrism's built-in
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
    /// the implementation BEFORE <c>AddAgentPrism()</c>: a <c>TryAdd</c>
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
    /// builder.AddAgentPrism()
    ///        .RequireCustomBinding&lt;IRunAuthorizationHandler&gt;()
    ///        .RequireCustomBinding&lt;IToolAuthorizationHandler&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder RequireCustomBinding<T>()
        where T : class;
}
