using Microsoft.Agents.AI.Workflows;

namespace AgentPrism;

/// <summary>
/// The registry of workflow functions built from
/// <see cref="WorkflowFunctionRegistration"/> entries.
/// </summary>
/// <remarks>
/// Mirrors <c>ToolRegistry</c>'s shape deliberately: both are K2 security
/// boundaries built once, at singleton construction time, from DI
/// registrations, and both reject a duplicate name immediately rather than
/// letting it surface later as a confusing "wrong function ran" bug.
/// </remarks>
internal sealed class WorkflowFunctionRegistry : IWorkflowFunctionCatalog
{
    private readonly Dictionary<string, Func<string, Executor>> _executorFactories;
    private readonly List<WorkflowFunctionDescriptor> _descriptors;

    /// <summary>Initializes a new registry from registrations.</summary>
    /// <param name="registrations">The function registrations.</param>
    /// <param name="services">
    /// The service provider used to resolve each function's own dependencies.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">The same name is registered more than once.</exception>
    public WorkflowFunctionRegistry(IEnumerable<WorkflowFunctionRegistration> registrations, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(services);

        _executorFactories = new Dictionary<string, Func<string, Executor>>(StringComparer.Ordinal);
        _descriptors = [];

        foreach (var registration in registrations)
        {
            var name = registration.Descriptor.Name;

            // Dependencies are resolved HERE, ONCE - when this singleton is
            // first built - not on every workflow compile. Microsoft Agent
            // Framework passes an empty service provider to executors at call
            // time (K-218); a function node's own dependencies hit the exact
            // same trap unless they are captured now.
            var createExecutor = registration.CreateExecutorFactory(services);

            if (!_executorFactories.TryAdd(name, createExecutor))
            {
                throw new AgentPrismException(
                    $"More than one workflow function is registered with name '{name}'. " +
                    "Function names must be unique.");
            }

            _descriptors.Add(registration.Descriptor);
        }

        _descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkflowFunctionDescriptor> List() => _descriptors;

    /// <inheritdoc />
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _executorFactories.ContainsKey(name);
    }

    /// <summary>Builds a fresh executor for the named function, ready to enter a graph.</summary>
    /// <param name="name">The registered function name.</param>
    /// <param name="executorId">The id the executor takes in the graph.</param>
    /// <returns>A new executor instance.</returns>
    /// <exception cref="AgentPrismException">No function is registered with the given name.</exception>
    public Executor CreateExecutor(string name, string executorId)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(executorId);

        if (!_executorFactories.TryGetValue(name, out var factory))
        {
            throw new AgentPrismException(
                $"There is no workflow function registered with name '{name}'. " +
                "Register it with AddWorkflowFunction() before referencing it from a workflow definition.");
        }

        return factory(executorId);
    }
}
