using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// A registry that combines tools registered in code with those discovered
/// from remote MCP servers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Code always wins.</strong> An MCP tool with the same name can
/// never replace a tool registered in code. Otherwise, a remote server
/// could take over a local tool's name and silently hijack the agent's
/// behavior. This is a natural extension of the code-only tools rule.
/// </para>
/// <para>
/// MCP tools are resolved <strong>per tenant</strong>: servers are
/// registered per tenant, and a tool coming from one tenant's server is not
/// visible to another tenant.
/// </para>
/// </remarks>
internal sealed class McpToolRegistry : IToolRegistry
{
    private readonly IToolRegistry _codeTools;
    private readonly McpToolCatalog _catalog;
    private readonly ITenantContext _tenantContext;

    internal McpToolRegistry(IToolRegistry codeTools, McpToolCatalog catalog, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(codeTools);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _codeTools = codeTools;
        _catalog = catalog;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> List()
    {
        var code = _codeTools.List();
        var remote = _catalog.ForTenant(_tenantContext.TenantId).Descriptors;

        if (remote.Count == 0)
        {
            return code;
        }

        var merged = new List<ToolDescriptor>(code.Count + remote.Count);
        merged.AddRange(code);

        var codeNames = new HashSet<string>(code.Select(static descriptor => descriptor.Name), StringComparer.Ordinal);

        foreach (var descriptor in remote)
        {
            if (!codeNames.Contains(descriptor.Name))
            {
                merged.Add(descriptor);
            }
        }

        merged.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return merged;
    }

    /// <inheritdoc />
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _codeTools.TryGet(name, out tool)
            || _catalog.ForTenant(_tenantContext.TenantId).TryGet(name, out tool);
    }
}
