using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Tracon;

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
/// <para>
/// It also tells where a tool comes from (<see cref="IToolSourceResolver"/>):
/// a standing approval rule records the endpoint of the server a tool was
/// discovered from and holds only while that endpoint stays the same.
/// </para>
/// <para>
/// And it tells which connection a tool is bound to (<see cref="IToolBindingResolver"/>):
/// the compiled-agent cache key carries it, so an agent whose tools call a replaced
/// or closed connection is never served again.
/// </para>
/// </remarks>
internal sealed class McpToolRegistry : IToolRegistry, IVerifiedToolRegistry, IToolSourceResolver, IToolBindingResolver
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

    /// <inheritdoc />
    /// <remarks>
    /// A code tool wins its name here exactly as in <see cref="TryGet"/>: a tool
    /// served from code has no remote source, even when a server also produced
    /// the same name.
    /// </remarks>
    public string? GetSourceFingerprint(string tenantId, string toolName)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(toolName);

        return _codeTools.TryGet(toolName, out _)
            ? null
            : _catalog.ForTenant(tenantId).GetSourceFingerprint(toolName);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A code tool wins its name here exactly as in <see cref="TryGet"/> and adds
    /// nothing: it is fixed for the life of the process. Every other name adds its
    /// <see cref="McpTenantTools.GetBindingFingerprint"/>, read from ONE snapshot of
    /// the tenant <see cref="TryGet"/> reads.
    /// </para>
    /// <para>
    /// Tool names are part of the definition, and the definition's own fingerprint is
    /// part of the same key, so the plain <c>name=binding;</c> form cannot make two
    /// different definitions collide.
    /// </para>
    /// </remarks>
    public string GetBindingFingerprint(IReadOnlyList<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        McpTenantTools? snapshot = null;
        System.Text.StringBuilder? fingerprint = null;

        foreach (var name in toolNames)
        {
            if (_codeTools.TryGet(name, out _))
            {
                continue;
            }

            snapshot ??= _catalog.ForTenant(_tenantContext.TenantId);

            (fingerprint ??= new System.Text.StringBuilder("mcp:"))
                .Append(name)
                .Append('=')
                .Append(snapshot.GetBindingFingerprint(name))
                .Append(';');
        }

        return fingerprint?.ToString() ?? string.Empty;
    }
}
