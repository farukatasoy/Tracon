using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Binds functions registered with <c>AddWorkflowFunction</c> to a workflow graph defined in code.
/// </summary>
/// <remarks>
/// <para>
/// A stored workflow chain gets, for every function node, a time limit, a retry policy, a budget gate, a
/// concurrency limit, a cost line that names the node and a strict read of its settings. A graph built in
/// code with its own executors gets none of them: they live in a wrapper that only the stored chain's
/// compiler could reach, and the budget gate cannot be built in consumer code at all. This helper hands the
/// same executor to a graph built in code - the same registry entry, the same wrapper, the same order.
/// </para>
/// <para>
/// <strong>The executor's id is the function's name.</strong> The node name in the run's events, in the
/// cost lines and in the checkpoint is the name the function was registered with, so use a function once in a
/// graph; to use the same code twice, register it under a second name.
/// </para>
/// <example>
/// <code>
/// tracon.AddWorkflow("review-and-revise", services =>
/// {
///     var validate = services.GetWorkflowFunction("validate");
///     var render = services.GetWorkflowFunction("render", step => step.Timeout = TimeSpan.FromMinutes(5));
///
///     return new WorkflowBuilder(validate)
///         .AddEdge(validate, render)
///         .WithOutputFrom(render)
///         .WithName("review-and-revise")
///         .Build();
/// });
/// </code>
/// </example>
/// </remarks>
public static class WorkflowFunctionBinding
{
    /// <summary>Binds a registered function to a workflow graph defined in code.</summary>
    /// <param name="services">The service provider.</param>
    /// <param name="functionName">The name the function was registered with.</param>
    /// <returns>The executor binding to put in the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="functionName"/> is empty.</exception>
    /// <exception cref="TraconException">
    /// No function is registered with that name. The graph is built for every run, so the run ends
    /// <c>Failed</c> before its first node, and the message names the workflow and the function.
    /// </exception>
    /// <remarks>
    /// Everything the other overload says applies here too. The function runs with its registered time
    /// limit, retry policy, concurrency limit and budget gate, and with no settings.
    /// </remarks>
    public static ExecutorBinding GetWorkflowFunction(this IServiceProvider services, string functionName)
        => GetWorkflowFunction(services, functionName, static _ => { });

    /// <summary>Binds a registered function to a workflow graph defined in code, with the node's own timeout and settings.</summary>
    /// <param name="services">The service provider.</param>
    /// <param name="functionName">The name the function was registered with.</param>
    /// <param name="configure">Sets the node's timeout and settings; called once, here.</param>
    /// <returns>The executor binding to put in the graph.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="functionName"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="WorkflowFunctionStepOptions.Timeout"/> is outside one millisecond to about 49.7 days.
    /// </exception>
    /// <exception cref="TraconException">
    /// No function is registered with that name, or the settings do not fit the settings type the function
    /// declares (or the function declares none). The graph is built for every run, so the run ends
    /// <c>Failed</c> before its first node, and the message names the workflow, the function and the member.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>The graph is built again for every run</strong>, and so is the executor: Microsoft Agent
    /// Framework does not allow one executor instance to serve two runs. The settings are read here, once
    /// per build, so the handler sees exactly the object that was checked.
    /// </para>
    /// <para>
    /// <strong>Input and output types are the function's.</strong> A node must take what the node before it
    /// passes on; Microsoft Agent Framework drops a message no handler takes, without an error. The chain
    /// check a stored workflow gets at save time does not run for a graph built in code.
    /// </para>
    /// <example>
    /// <code>
    /// tracon.AddWorkflow("narrated", services =>
    /// {
    ///     var tts = services.GetWorkflowFunction("tts", step =>
    ///     {
    ///         step.Timeout = TimeSpan.FromMinutes(10);
    ///         step.SetSettings(new TtsSettings { Voice = "mira" });
    ///     });
    ///
    ///     return new WorkflowBuilder(tts).WithOutputFrom(tts).WithName("narrated").Build();
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    public static ExecutorBinding GetWorkflowFunction(
        this IServiceProvider services,
        string functionName,
        Action<WorkflowFunctionStepOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new WorkflowFunctionStepOptions();
        configure(options);

        if (options.Timeout is { } timeout && !TimerPeriod.IsValid(timeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                timeout,
                TimerPeriod.Failure($"{nameof(WorkflowFunctionStepOptions)}.{nameof(WorkflowFunctionStepOptions.Timeout)}", timeout));
        }

        var registry = services.GetRequiredService<WorkflowFunctionRegistry>();

        if (!registry.Contains(functionName))
        {
            throw new WorkflowGraphBindingException(
                $"There is no workflow function registered with name '{functionName}'. " +
                "Register it with AddWorkflowFunction() before binding it to a graph.");
        }

        try
        {
            return ExecutorBindingExtensions.BindExecutor(
                registry.CreateExecutor(functionName, functionName, options.Settings, options.Timeout));
        }
        catch (WorkflowFunctionSettingsException exception)
        {
            throw new WorkflowGraphBindingException(
                $"{exception.Message} Fix the settings passed to GetWorkflowFunction(\"{functionName}\").",
                exception);
        }
    }
}
