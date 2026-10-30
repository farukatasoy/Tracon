using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The binding fingerprint <c>UseMcp()</c>'s registry gives the compiled-agent
/// cache key. The registry is resolved through real DI, so the compiler
/// sees exactly this instance.
/// </summary>
public sealed class McpToolRegistryBindingTests
{
    /// <summary>
    /// A code tool wins its name here as in <see cref="IToolRegistry.TryGet"/> and
    /// adds nothing, so a definition with only code tools keeps the key it had
    /// before MCP took part in it. A remote name adds its binding; before any
    /// discovery that binding is "absent".
    /// </summary>
    [Fact]
    public async Task A_code_tool_adds_nothing_and_a_remote_name_adds_its_binding()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTracon()
            .AddTool(() => "local", "crm_lookup", "A code tool that shares a server-style name.")
            .UseMcp();

        await using var provider = services.BuildServiceProvider();

        var bindings = provider.GetRequiredService<IToolRegistry>().ShouldBeAssignableTo<IToolBindingResolver>().ShouldNotBeNull();

        bindings.GetBindingFingerprint([]).ShouldBeEmpty();
        bindings.GetBindingFingerprint(["crm_lookup"]).ShouldBeEmpty("code always wins its name");
        bindings.GetBindingFingerprint(["crm_lookup", "docs_search"]).ShouldBe("mcp:docs_search=absent;");
    }
}
