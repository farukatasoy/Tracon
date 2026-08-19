using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that register workflow execution.</summary>
public static class AgentPrismWorkflowsBuilderExtensions
{
    /// <summary>
    /// Registers the workflow execution engine. Agents from the catalog can be
    /// chained together with ready-made patterns, and every execution produces
    /// a <c>runs</c> row.
    /// </summary>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <param name="configure">The option customizer.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Security boundary.</strong> A workflow defined from the UI does
    /// not produce new behavior: it only sequences catalog agents with
    /// ready-made patterns whose order is fixed. A free-form graph - custom
    /// <c>Executor</c> types, conditional edges, sub-workflows - is only
    /// defined in code, with <see cref="AddWorkflow"/>. Design rule K2 is
    /// preserved this way.
    /// </para>
    /// <para>
    /// The definition and checkpoint stores are already registered by
    /// <c>AddAgentPrism()</c>; this call only turns <em>execution</em> on.
    /// While the engine is not registered, the HTTP layer can still list and
    /// manage definitions - only the execution endpoint returns <c>501</c>.
    /// </para>
    /// <para>
    /// <see cref="AgentPrismWorkflowOptions.SectionName"/> (<c>AgentPrism:Workflows</c>)
    /// is BOUND from <c>IConfiguration</c> (K-402) - the same contract as
    /// every other <c>Use*()</c> extension (<c>UseOpenAI</c>,
    /// <c>UsePostgreSql</c>, <c>UseSkillScripts</c>, and so on).
    /// <paramref name="configure"/> runs AFTER this binding, so code can still
    /// override the config.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseWorkflows()
    ///        .UseUI();
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseWorkflows(
        this IAgentPrismBuilder builder,
        Action<AgentPrismWorkflowOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services
            .AddOptions<AgentPrismWorkflowOptions>()
            .BindConfiguration(AgentPrismWorkflowOptions.SectionName);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // The cache MUST be SINGLETON: executor identity stability - and
        // therefore resuming from checkpoints - depends on it.
        services.TryAddSingleton<WorkflowAgentCache>();

        // Registered here TOO (not only by AddWorkflowFunction), so that a
        // definition compiles into an EMPTY registry - not a missing
        // dependency - when no function was ever registered in code. DoD:
        // behavior with zero function nodes must stay identical.
        services.TryAddSingleton<WorkflowFunctionRegistry>();
        services.TryAddSingleton<IWorkflowFunctionCatalog>(
            static provider => provider.GetRequiredService<WorkflowFunctionRegistry>());

        services.TryAddSingleton<WorkflowDefinitionCompiler>();
        services.TryAddSingleton<WorkflowCatalog>();
        services.TryAddSingleton<IWorkflowRunner, WorkflowRunner>();

        return builder;
    }

    /// <summary>
    /// Defines a factory-based workflow in code. How the graph is built is
    /// entirely up to the caller.
    /// </summary>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="factory">The factory that builds the graph.</param>
    /// <param name="description">A short description.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <remarks>
    /// A workflow defined in code <strong>takes precedence over</strong> a
    /// database definition with the same name. The same rule applies to the
    /// agent catalog (K-019): someone with write access to the database cannot
    /// take over a behavior registered in code.
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseWorkflows()
    ///        .AddWorkflow("triage", provider => BuildTriageGraph(provider), "Routes an incoming ticket.");
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddWorkflow(
        this IAgentPrismBuilder builder,
        string name,
        Func<IServiceProvider, Workflow> factory,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddSingleton(new CodeWorkflowRegistration(name, description, factory));

        return builder;
    }
}
