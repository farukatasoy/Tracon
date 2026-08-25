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
    /// The escape hatch: anything AgentPrism does not model is registered here,
    /// and a registration made before <c>AddAgentPrism()</c> wins over
    /// AgentPrism's own, because every AgentPrism service is registered with
    /// <c>TryAdd</c>.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .Services.AddSingleton&lt;IOrderGateway, OrderGateway&gt;();
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
    /// <param name="requiresApproval">Whether explicit approval is required before the call.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// The AOT-safe overload: the caller supplies the built
    /// <see cref="AIFunction"/>, so no reflection is involved.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .AddTool(refundTool, requiresApproval: true);
    /// </code>
    /// </example>
    /// </remarks>
    IAgentPrismBuilder AddTool(AIFunction tool, bool requiresApproval);

    /// <summary>
    /// Builds a tool from a method and registers it.
    /// </summary>
    /// <param name="method">The method to expose as a tool.</param>
    /// <param name="name">The tool name. If left empty, the method name is used.</param>
    /// <param name="description">A description that tells the model when to call the tool.</param>
    /// <param name="requiresApproval">Whether explicit approval is required before the call.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <remarks>
    /// This overload uses reflection through <c>AIFunctionFactory</c> and is
    /// therefore not safe under trimming or native AOT scenarios. Applications
    /// targeting AOT should use the <see cref="AddTool(AIFunction, bool)"/>
    /// overload instead.
    /// </remarks>
    [RequiresUnreferencedCode("Building a tool from a method uses reflection; type information may be lost in trimmed applications.")]
    [RequiresDynamicCode("Building a tool from a method may require code generation at runtime.")]
    IAgentPrismBuilder AddTool(Delegate method, string? name = null, string? description = null, bool requiresApproval = false);

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
    /// <see cref="AddTool(AIFunction, bool)"/> overload instead.
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
}
