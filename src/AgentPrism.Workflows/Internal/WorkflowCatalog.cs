using Microsoft.Agents.AI.Workflows;

namespace AgentPrism;

/// <summary>
/// Combines the workflows defined in code and stored in the database into a
/// single catalog.
/// </summary>
/// <remarks>
/// On a name clash, <strong>code wins</strong>. The same rule applies to the
/// agent catalog (K-019): a definition in code is validated at build time, and
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

        _codeWorkflows = codeWorkflows.ToDictionary(
            static registration => registration.Name,
            StringComparer.Ordinal);
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
            descriptors[registration.Name] = new WorkflowDescriptor
            {
                Name = registration.Name,
                Description = registration.Description,
                Origin = AgentDefinitionOrigin.Code,
            };
        }

        return [.. descriptors.Values.OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)];
    }

    /// <summary>Gets the summary of a single workflow.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary; <see langword="null"/> if it does not exist.</returns>
    public async ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_codeWorkflows.TryGetValue(name, out var registration))
        {
            return new WorkflowDescriptor
            {
                Name = registration.Name,
                Description = registration.Description,
                Origin = AgentDefinitionOrigin.Code,
            };
        }

        var definition = await _store
            .GetAsync(_tenantContext.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return definition is null ? null : Describe(definition);
    }

    /// <summary>Turns the named workflow into a runnable graph.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The built graph; <see langword="null"/> if the workflow is not in the catalog.</returns>
    /// <exception cref="AgentPrismException">The definition is invalid, or an agent cannot be found.</exception>
    /// <remarks>
    /// The graph is <strong>rebuilt on every run</strong>, never cached.
    /// Reason: Microsoft Agent Framework executors carry state, and using the
    /// same <see cref="Workflow"/> instance for two concurrent runs would
    /// share that state between them. The build cost is negligible next to a
    /// single model call.
    /// </remarks>
    public async ValueTask<Workflow?> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_codeWorkflows.TryGetValue(name, out var registration))
        {
            return registration.Factory(_services);
        }

        var definition = await _store
            .GetAsync(_tenantContext.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return definition is null
            ? null
            : await _compiler.CompileAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    private static WorkflowDescriptor Describe(WorkflowDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Version = definition.Version,
            UpdatedAt = definition.UpdatedAt,
        };
}
