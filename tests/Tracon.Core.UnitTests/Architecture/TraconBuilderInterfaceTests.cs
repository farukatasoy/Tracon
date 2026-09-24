using System.Reflection;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// <see cref="ITraconBuilder"/> carries <see cref="ITraconBuilder.Services"/>
/// and nothing else; every registration capability is an extension method.
/// </summary>
/// <remarks>
/// A member added to the interface breaks every implementation of it once the
/// public API ships, and neither the constructor ratchet nor the type baseline
/// sees it. A new capability belongs in <see cref="TraconBuilderExtensions"/>
/// or in a package's own extension class.
/// </remarks>
public sealed class TraconBuilderInterfaceTests
{
    [Fact]
    public void The_builder_interface_declares_only_the_service_collection()
    {
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var members = typeof(ITraconBuilder).GetMembers(Everything).Select(static m => m.Name).Order(StringComparer.Ordinal);

        members.ToArray().ShouldBe(
            ["Services", "get_Services"],
            customMessage: "ITraconBuilder must stay a one-member interface (decision K-867): add a registration method as an extension, not as an interface member.");
    }

    [Fact]
    public void Builder_extensions_without_a_registration_prefix_are_entry_points()
    {
        // K-509: the receiver decides, not the name. These three would drop out
        // of the capability gates under a prefix-only rule.
        foreach (var name in new[] { "Configure", "RequireCustomBinding", "RequireProductionProfile", "Services" })
        {
            CapabilityEntryPoints.Names.Contains(name, StringComparer.Ordinal).ShouldBeTrue($"'{name}' is not counted as an entry point.");
        }
    }
}
