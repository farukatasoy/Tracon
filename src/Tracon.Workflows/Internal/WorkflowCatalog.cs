using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// Combines the workflows defined in code and stored in the database into a
/// single catalog.
/// </summary>
/// <remarks>
/// On a name clash, <strong>code wins</strong>. The same rule applies to the
/// agent catalog: a definition in code is validated at build time, and
/// someone with write access to the database cannot take over a behavior
/// registered in code.
/// </remarks>
internal sealed class WorkflowCatalog
{
    private readonly Dictionary<string, CodeWorkflowRegistration> _codeWorkflows;
    private readonly IWorkflowDefinitionStore _store;
    private readonly WorkflowDefinitionCompiler _compiler;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceProvider _services;

    /// <summary>Creates a new catalog.</summary>
    /// <param name="codeWorkflows">The workflows registered in code.</param>
    /// <param name="store">The database definition store.</param>
    /// <param name="compiler">The definition compiler.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="services">The service provider used by code factories.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same code-defined workflow name is registered more than once.</exception>
    public WorkflowCatalog(
        IEnumerable<CodeWorkflowRegistration> codeWorkflows,
        IWorkflowDefinitionStore store,
        WorkflowDefinitionCompiler compiler,
        ITenantContext tenantContext,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(codeWorkflows);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(services);

        _codeWorkflows = new Dictionary<string, CodeWorkflowRegistration>(StringComparer.Ordinal);

        foreach (var registration in codeWorkflows)
        {
            if (!_codeWorkflows.TryAdd(registration.Name, registration))
            {
                throw new TraconException(
                    $"More than one code-defined workflow is registered with name '{registration.Name}'. " +
                    "Workflow names must be unique.");
            }
        }

        _store = store;
        _compiler = compiler;
        _tenantContext = tenantContext;
        _services = services;
    }

    /// <summary>Lists every workflow in the catalog, ordered by name.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summaries.</returns>
    public async ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var descriptors = new Dictionary<string, WorkflowDescriptor>(StringComparer.Ordinal);

        var stored = await _store.ListAsync(_tenantContext.TenantId, cancellationToken).ConfigureAwait(false);

        foreach (var definition in stored)
        {
            descriptors[definition.Name] = Describe(definition);
        }

        // Code registrations are written LAST and overwrite the database entry.
        foreach (var registration in _codeWorkflows.Values)
        {
            descriptors[registration.Name] = Describe(registration);
        }

        return [.. descriptors.Values.OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)];
    }

    /// <summary>Gets the summary of a single workflow.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary; <see langword="null"/> if it does not exist.</returns>
    public ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
        => GetAsync(name, version: null, cancellationToken);

    /// <summary>Gets the summary of a single workflow, as one of its versions describes it.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="version">The stored version to describe; <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary; <see langword="null"/> if the workflow does not exist.</returns>
    /// <exception cref="TraconException">The workflow has no such version.</exception>
    public async ValueTask<WorkflowDescriptor?> GetAsync(
        string name,
        int? version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_codeWorkflows.TryGetValue(name, out var registration))
        {
            return Describe(registration);
        }

        var found = await FindAsync(name, version, cancellationToken).ConfigureAwait(false);

        return found?.Definition is { } definition ? Describe(definition) : null;
    }

    /// <summary>
    /// Reads the named workflow, and nothing else: no graph is built and no agent is touched.
    /// </summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="version">
    /// The stored version to read. <see langword="null"/> reads the current one. Ignored for a
    /// workflow defined in code, which has no versions.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was found; <see langword="null"/> if the workflow is not in the catalog.</returns>
    /// <exception cref="TraconException">The workflow exists but has no such version.</exception>
    /// <remarks>
    /// <para>
    /// A run reads the definition ONCE, here, and takes everything it needs from that read: the
    /// graph (<see cref="CompileAsync"/>), the parameter schema, the run timeout and the version
    /// it stamps on its row. Two reads could see two versions of a workflow being edited.
    /// </para>
    /// <para>
    /// A named version requires the workflow to exist now. History outlives a delete (so a
    /// number is never reused), but a run is not resumed into a workflow that was removed: the
    /// definition is the thing the operator removed.
    /// </para>
    /// </remarks>
    public async ValueTask<FoundWorkflow?> FindAsync(
        string name,
        int? version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_codeWorkflows.TryGetValue(name, out var registration))
        {
            return new FoundWorkflow(registration, null);
        }

        var current = await _store
            .GetAsync(_tenantContext.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return null;
        }

        if (version is not { } pinned || pinned == current.Version)
        {
            return new FoundWorkflow(null, current);
        }

        var definition = await _store
            .GetVersionAsync(_tenantContext.TenantId, name, pinned, cancellationToken)
            .ConfigureAwait(false);

        // Never the current definition in its place: an operator who named a version, and a
        // run that recorded one, must not be handed a different content in silence.
        return definition is null
            ? throw new TraconException(
                $"Workflow '{name}' has no version {pinned}. List its versions with " +
                $"GET /api/workflows/{name}/versions.")
            : new FoundWorkflow(null, definition);
    }

    /// <summary>Turns what <see cref="FindAsync"/> read into a runnable graph.</summary>
    /// <param name="found">The workflow that was read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The built graph.</returns>
    /// <exception cref="TraconException">The definition is invalid, or an agent cannot be found.</exception>
    /// <remarks>
    /// The graph is <strong>rebuilt on every run</strong>, never cached.
    /// Microsoft Agent Framework executors carry state, and using the
    /// same <see cref="Workflow"/> instance for two concurrent runs would
    /// share that state between them. The build cost is negligible next to a
    /// single model call.
    /// </remarks>
    public ValueTask<Workflow> CompileAsync(FoundWorkflow found, CancellationToken cancellationToken = default)
        => found.Code is { } registration
            ? new ValueTask<Workflow>(registration.Factory(_services))
            : _compiler.CompileAsync(found.Definition!, cancellationToken);

    /// <summary>Turns the named workflow into a runnable graph.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The built graph; <see langword="null"/> if the workflow is not in the catalog.</returns>
    /// <exception cref="TraconException">The definition is invalid, or an agent cannot be found.</exception>
    public async ValueTask<Workflow?> ResolveAsync(string name, CancellationToken cancellationToken = default)
        => await FindAsync(name, version: null, cancellationToken).ConfigureAwait(false) is { } found
            ? await CompileAsync(found, cancellationToken).ConfigureAwait(false)
            : null;

    /// <summary>A workflow as the catalog read it, before any graph is built.</summary>
    /// <param name="Code">The registration of a workflow defined in code; <see langword="null"/> for a stored one.</param>
    /// <param name="Definition">The stored definition; <see langword="null"/> for a workflow defined in code.</param>
    internal readonly record struct FoundWorkflow(CodeWorkflowRegistration? Code, WorkflowDefinition? Definition);

    private static WorkflowDescriptor Describe(WorkflowDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Nodes = definition.Nodes,
            Parameters = definition.Parameters,
            ResponseMode = definition.ResponseMode,
            RequestTimeout = definition.RequestTimeout,
            Version = definition.Version,
            UpdatedAt = definition.UpdatedAt,
        };

    // One builder for the two places a code workflow is described (the list and a single
    // read), so a new descriptor field cannot reach one and miss the other.
    private static WorkflowDescriptor Describe(CodeWorkflowRegistration registration)
        => new()
        {
            Name = registration.Name,
            Description = registration.Description,
            Origin = AgentDefinitionOrigin.Code,
            ResponseMode = registration.ResponseMode,
            RequestTimeout = registration.RequestTimeout,
        };
}
