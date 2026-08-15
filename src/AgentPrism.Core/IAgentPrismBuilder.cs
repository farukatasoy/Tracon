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
    IServiceCollection Services { get; }

    /// <summary>Modifies the runtime settings.</summary>
    /// <param name="configure">The settings modifier.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder Configure(Action<AgentPrismOptions> configure);

    /// <summary>Registers a tool.</summary>
    /// <param name="tool">The tool to register.</param>
    /// <param name="requiresApproval">Whether explicit approval is required before the call.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddTool(AIFunction tool, bool requiresApproval = false);

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
    IAgentPrismBuilder AddAgent(AgentDefinition definition);

    /// <summary>
    /// Defines a skill in code. A skill defined in code takes precedence over a
    /// runtime skill with the same name.
    /// </summary>
    /// <param name="skill">The skill to register.</param>
    /// <returns>The chain, for further configuration.</returns>
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

    /// <summary>Registers a model provider.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddModelProvider(IModelProvider provider);

    /// <summary>Registers a model provider through a factory.</summary>
    /// <param name="factory">The factory that produces the provider.</param>
    /// <returns>The chain, for further configuration.</returns>
    IAgentPrismBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory);

    /// <summary>
    /// Registers a custom eval check (Phase 18). Eval suites can reference it by
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
    /// </remarks>
    IAgentPrismBuilder AddEvalCheck(string kind, Microsoft.Agents.AI.EvalCheck check);
}
