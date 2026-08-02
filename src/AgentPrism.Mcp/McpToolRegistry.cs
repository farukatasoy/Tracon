using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Kodda kayitli tool'lari uzak MCP sunucularindan kesfedilenlerle birlestiren defter.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Kod her zaman kazanir.</strong> Ayni ada sahip bir MCP tool'u, kodda
/// kayitli tool'un yerine gecemez. Aksi olsaydi uzak bir sunucu, adini degistirerek
/// yerel bir tool'un yerini alabilir ve agent'in davranisini sessizce ele
/// gecirebilirdi. Bu, tasarim kurali K2'nin dogal uzantisidir.
/// </para>
/// <para>
/// MCP tool'lari <strong>kiraciya gore</strong> cozulur: sunucular kiraci basina
/// kayitlidir ve bir kiracinin sunucusundan gelen tool baska bir kiracida
/// gorunmez.
/// </para>
/// </remarks>
public sealed class McpToolRegistry : IToolRegistry
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
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _codeTools.TryGet(name, out tool)
            || _catalog.ForTenant(_tenantContext.TenantId).TryGet(name, out tool);
    }
}
