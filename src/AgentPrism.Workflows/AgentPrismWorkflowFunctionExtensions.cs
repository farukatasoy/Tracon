using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Extensions that register a code function as a workflow node.</summary>
public static class AgentPrismWorkflowFunctionExtensions
{
    /// <summary>
    /// Registers a function that a <see cref="WorkflowKind.Sequential"/>
    /// workflow can use as a node, mixed in with agents from the catalog.
    /// </summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <param name="name">
    /// The function's unique name. A <see cref="WorkflowDefinition"/> node of
    /// kind <see cref="WorkflowNodeKind.Function"/> points to it by this name.
    /// </param>
    /// <param name="factory">
    /// Resolves the function's dependencies from the service provider and
    /// returns the handler that runs on every call.
    /// </param>
    /// <param name="description">A short description shown in the function catalog.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the required parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Security boundary (K2).</strong> A workflow definition can only
    /// point to a function registered here, by name. The function's
    /// <em>code</em> is never written from the UI or the database - the same
    /// boundary <c>AddTool</c> draws for tools, and the same boundary
    /// <see cref="AgentPrismWorkflowsBuilderExtensions.AddWorkflow"/> draws
    /// for a free-form graph.
    /// </para>
    /// <para>
    /// <strong><paramref name="factory"/> runs exactly once</strong>, when the
    /// function registry is first built - not once per workflow run. Resolve
    /// every dependency the handler needs inside <paramref name="factory"/>;
    /// the handler itself receives no service provider at call time
    /// (<c>IWorkflowContext</c> does not carry one, the same constraint
    /// tools hit under K-218).
    /// </para>
    /// <para>
    /// 🚨 <strong>The handler must be thread-safe.</strong> Because
    /// <paramref name="factory"/> runs once, every workflow compile - and
    /// therefore every concurrent run of this workflow, and every other
    /// workflow that references the same function name - shares the exact
    /// same handler closure. A handler that captures mutable state (a
    /// counter, a non-thread-safe client) must guard it itself.
    /// </para>
    /// <para>
    /// Only <see cref="WorkflowKind.Sequential"/> supports function nodes.
    /// Microsoft Agent Framework's ready-made builders for the other four
    /// patterns accept only agents; splicing a function into them would mean
    /// hand-writing their orchestration logic, which is out of scope.
    /// </para>
    /// <para>
    /// 🚨 <strong>The handler must be idempotent when the workflow enables
    /// checkpointing</strong> (the default). Measured (phase 71): resuming
    /// from the run's LATEST checkpoint after it has already completed does
    /// <em>not</em> call the handler again - the checkpoint already reflects
    /// the finished graph, so there is nothing left to run. But resuming from
    /// an <em>earlier</em> checkpoint - the shape a real crash recovery takes,
    /// naming a checkpoint written before this node's own super-step - replays
    /// that super-step, and the handler runs again with the same input. A
    /// handler with a real side effect (a file write, an HTTP call, a database
    /// insert) must therefore tolerate being called more than once for the
    /// same logical step.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder AddWorkflowFunction<TInput, TOutput>(
        this IAgentPrismBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        var descriptor = new WorkflowFunctionDescriptor
        {
            Name = name,
            Description = description,
            InputType = typeof(TInput),
            OutputType = typeof(TOutput),
        };

        builder.Services.AddSingleton(new WorkflowFunctionRegistration(
            descriptor,
            services =>
            {
                var handler = factory(services);

                return executorId => new FunctionExecutor<TInput, TOutput>(executorId, handler);
            }));

        builder.Services.TryAddSingleton<WorkflowFunctionRegistry>();
        builder.Services.TryAddSingleton<IWorkflowFunctionCatalog>(
            static services => services.GetRequiredService<WorkflowFunctionRegistry>());

        return builder;
    }
}
