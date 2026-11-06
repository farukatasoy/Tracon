using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The package half of the reload guard. The core half (<c>ConfigurationReloadTests</c>) composes the core alone; this
/// one composes each package the way a host does and lists every options type that has a change token source.
/// </summary>
/// <remarks>
/// <para>
/// Tracon binds its sections by hand, so a configuration provider that reloads never reaches them, and a change to the
/// configuration needs a restart. The workflow options used to be read by the framework's configuration binder
/// (<c>BindConfiguration</c>), which registers a change token source on its own and split the settings between the
/// readers; they are bound by hand now. This test is what keeps the statement true for every package: an options type
/// that starts to follow a reload fails here and says which.
/// </para>
/// </remarks>
public sealed class PackageConfigurationReloadTests
{
    [Fact]
    public void No_options_type_of_a_package_has_a_change_token_source()
    {
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        var composed = 0;

        foreach (var scalar in OptionScalarCatalog.All().GroupBy(static scalar => scalar.RootType).Select(static group => group.First()))
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(scalar.BaseKeys).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);

            var section = configuration.GetSection("Tracon");
            var builder = services.AddTracon(section);
            scalar.Compose?.Invoke(builder, section);
            composed++;

            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Options.IOptionsChangeTokenSource<>))
                {
                    sources.Add(descriptor.ServiceType.GetGenericArguments()[0].Name);
                }
            }
        }

        // A scan that composed few options types would pass for the wrong reason.
        composed.ShouldBeGreaterThanOrEqualTo(10);
        sources.ShouldBeEmpty(
            "no Tracon options type follows a configuration reload, and the documentation says that a change needs a restart. " +
            "An options type with a source changes that statement: decide each reader of it first.");
    }
}
