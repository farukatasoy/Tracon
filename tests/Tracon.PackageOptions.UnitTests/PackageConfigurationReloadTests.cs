using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The package half of the reload guard. The core half (<c>ConfigurationReloadTests</c>) composes the core alone; this
/// one composes each package the way a host does and lists every options type that has a change token source.
/// </summary>
/// <remarks>
/// <para>
/// Tracon binds its sections by hand, so a configuration provider that reloads never reaches them. One call is the
/// exception: <c>UseWorkflows</c> binds its options through the framework's configuration binder
/// (<c>BindConfiguration</c>), and the binder registers a change token source on its own. The documentation states that
/// exception; this test is what keeps the statement true. A second package that starts to follow a reload, or a
/// workflow options type that stops, fails here and says which.
/// </para>
/// </remarks>
public sealed class PackageConfigurationReloadTests
{
    [Fact]
    public void The_workflow_options_are_the_only_ones_with_a_change_token_source()
    {
        var sources = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var scalar in OptionScalarCatalog.All().GroupBy(static scalar => scalar.RootType).Select(static group => group.First()))
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(scalar.BaseKeys).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);

            var section = configuration.GetSection("Tracon");
            var builder = services.AddTracon(section);
            scalar.Compose?.Invoke(builder, section);

            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Options.IOptionsChangeTokenSource<>))
                {
                    sources.Add(descriptor.ServiceType.GetGenericArguments()[0].Name);
                }
            }
        }

        sources.ShouldBe(
            ["TraconWorkflowOptions"],
            "only the workflow options follow a configuration reload (UseWorkflows binds them through the framework's binder). " +
            "Any other options type with a source changes what the documentation says about a restart: decide each reader first.");
    }
}
