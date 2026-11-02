using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Tracon.Mcp.UnitTests;

/// <summary>A configuration with no key, for a server definition that names none.</summary>
internal sealed class EmptyMcpConfiguration : IConfiguration
{
    public string? this[string key]
    {
        get => null;
        set => throw new NotSupportedException();
    }

    public IEnumerable<IConfigurationSection> GetChildren() => [];

    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}
