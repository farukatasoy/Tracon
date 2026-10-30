using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The registry of workflow functions built from
/// <see cref="WorkflowFunctionRegistration"/> entries.
/// </summary>
/// <remarks>
/// Mirrors <c>ToolRegistry</c>'s shape deliberately: both are the code-only tools rule security
/// boundaries built once, at singleton construction time, from DI
/// registrations, and both reject a duplicate name immediately rather than
/// letting it surface later as a confusing "wrong function ran" bug.
/// </remarks>
internal sealed class WorkflowFunctionRegistry : IWorkflowFunctionCatalog
{
    private readonly Dictionary<string, Func<string, JsonElement?, TimeSpan?, Executor>> _executorFactories;
    private readonly Dictionary<string, WorkflowFunctionDescriptor> _descriptorsByName;
    private readonly List<WorkflowFunctionDescriptor> _descriptors;
    private readonly IServiceProvider _services;
    private readonly ILogger? _logger;

    /// <summary>Initializes a new registry from registrations.</summary>
    /// <param name="registrations">The function registrations.</param>
    /// <param name="services">
    /// The service provider used to resolve each function's own dependencies.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same name is registered more than once.</exception>
    public WorkflowFunctionRegistry(IEnumerable<WorkflowFunctionRegistration> registrations, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _executorFactories = new Dictionary<string, Func<string, JsonElement?, TimeSpan?, Executor>>(StringComparer.Ordinal);
        _descriptorsByName = new Dictionary<string, WorkflowFunctionDescriptor>(StringComparer.Ordinal);
        _descriptors = [];

        _logger = services.GetService(typeof(ILoggerFactory)) is ILoggerFactory loggerFactory
            ? loggerFactory.CreateLogger<WorkflowFunctionRegistry>()
            : null;

        foreach (var registration in registrations)
        {
            var name = registration.Descriptor.Name;

            // Dependencies are resolved HERE, ONCE - when this singleton is
            // first built - not on every workflow compile. Microsoft Agent
            // Framework passes an empty service provider to executors at call
            // time; a function node's own dependencies hit the exact
            // same trap unless they are captured now.
            var createExecutor = registration.CreateExecutorFactory(services);

            if (!_executorFactories.TryAdd(name, createExecutor))
            {
                throw new TraconException(
                    $"More than one workflow function is registered with name '{name}'. " +
                    "Function names must be unique.");
            }

            var descriptor = WithSchema(registration.Descriptor, _logger);

            _descriptorsByName.Add(name, descriptor);
            _descriptors.Add(descriptor);
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

    /// <inheritdoc />
    public string? ValidateSettings(string name, JsonElement? settings)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_descriptorsByName.TryGetValue(name, out var descriptor))
        {
            return NotRegistered(name);
        }

        if (descriptor.SettingsType is not { } settingsType)
        {
            return settings is null ? null : WorkflowFunctionSettings.NoSettings(name);
        }

        // The cap guards the definition, which is written whole on every save and read
        // on every run. It is a save-time rule only: a stored definition that predates
        // a lowered cap still runs.
        if (settings is { } value)
        {
            var limit = MaxSettingsLength();

            if (Encoding.UTF8.GetByteCount(value.GetRawText()) > limit)
            {
                return $"Function node '{name}' has settings larger than {limit} bytes of JSON. " +
                       "Keep the settings small: a node's settings are values, not data. " +
                       $"The limit is {nameof(TraconWorkflowOptions)}.{nameof(TraconWorkflowOptions.MaxNodeSettingsLength)}.";
            }
        }

        try
        {
            _ = WorkflowFunctionSettings.Deserialize(settingsType, name, settings);

            return null;
        }
        catch (WorkflowFunctionSettingsException exception)
        {
            // The answer to the operator is the short message. When the refusal came from the
            // consumer's own setter or constructor, the exception itself goes to the log, so
            // the developer can see what their code objected to.
            if (WorkflowFunctionSettings.IsRaisedByConsumerCode(exception))
            {
                _logger?.LogWarning(
                    exception.InnerException,
                    "The settings type of workflow function {Function} rejected a value while a workflow was being saved.",
                    name);
            }

            return exception.Message;
        }
    }

    /// <summary>Builds a fresh executor for the named function, ready to enter a graph.</summary>
    /// <param name="name">The registered function name.</param>
    /// <param name="executorId">The id the executor takes in the graph.</param>
    /// <param name="settings">
    /// The node's settings, read into the function's settings type now - once per
    /// compile - so the function sees exactly the object that was checked.
    /// <see langword="null"/> for a node without settings.
    /// </param>
    /// <param name="timeout">
    /// The node's own timeout (<see cref="WorkflowNodeReference.Timeout"/>);
    /// <see langword="null"/> uses the timeout the function was registered with, if any.
    /// </param>
    /// <returns>A new executor instance.</returns>
    /// <exception cref="TraconException">No function is registered with the given name.</exception>
    /// <exception cref="WorkflowFunctionSettingsException">
    /// The settings do not fit the function's settings type, or the function declares none.
    /// </exception>
    public Executor CreateExecutor(string name, string executorId, JsonElement? settings = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(executorId);

        if (!_executorFactories.TryGetValue(name, out var factory))
        {
            throw new TraconException(NotRegistered(name));
        }

        // A function that declares no settings must not be handed any. The save-time
        // check says so too, but a function can lose its settings type between
        // deployments, and "ignored" would look like a setting that works.
        if (settings is not null && _descriptorsByName[name].SettingsType is null)
        {
            throw new WorkflowFunctionSettingsException(WorkflowFunctionSettings.NoSettings(name));
        }

        // The node's value wins over the registration's; neither means the
        // function has no limit of its own and only the run's deadline stops it.
        return factory(executorId, settings, timeout ?? _descriptorsByName[name].Timeout);
    }

    private static string NotRegistered(string name)
        => $"There is no workflow function registered with name '{name}'. " +
           "Register it with AddWorkflowFunction() before referencing it from a workflow definition.";

    /// <summary>
    /// Adds the schema of the settings type, made once when the registry is built.
    /// </summary>
    /// <remarks>
    /// The schema only helps an operator write a valid object; the read is what checks
    /// it. A type the schema exporter cannot describe costs the console its field list,
    /// never the function - and it is reported as a bare object schema, NOT as no schema:
    /// a missing schema means "this function takes no settings" to the console, and a
    /// function that does take them must never look like one that does not.
    /// </remarks>
    private static WorkflowFunctionDescriptor WithSchema(WorkflowFunctionDescriptor descriptor, ILogger? logger)
    {
        if (descriptor.SettingsType is not { } settingsType || descriptor.SettingsSchema is not null)
        {
            return descriptor;
        }

        try
        {
            return descriptor with { SettingsSchema = WorkflowFunctionSettings.CreateSchema(settingsType) };
        }

        // No token governs this (the registry is built once, synchronously), so every exception is caught.
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "The settings schema of workflow function {Function} could not be produced. " +
                "The function still works; the console lists no fields for its settings.",
                descriptor.Name);

            return descriptor with { SettingsSchema = WorkflowFunctionSettings.UnknownSchema };
        }
    }

    private int MaxSettingsLength()
        => (_services.GetService(typeof(IOptionsMonitor<TraconWorkflowOptions>)) as IOptionsMonitor<TraconWorkflowOptions>)
               ?.CurrentValue.MaxNodeSettingsLength
           ?? new TraconWorkflowOptions().MaxNodeSettingsLength;
}
