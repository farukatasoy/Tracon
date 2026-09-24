using System.Globalization;
using System.Reflection;
using System.Text;

namespace Tracon;

/// <summary>
/// Decides whether the Tracon package assemblies loaded into this process come
/// from one release.
/// </summary>
/// <remarks>
/// <para>
/// Every Tracon package ships on one version line and depends on its siblings
/// at exactly its own version. A graph can still mix releases: a package
/// published before that exact range existed accepts any newer sibling, and a
/// direct reference overrides the range with only a restore warning (NU1608).
/// The process then starts and fails later, at the first call into a member the
/// other release no longer has. The alignment check turns that into one startup
/// error that names every version.
/// </para>
/// <para>
/// The family is a fixed list of assembly names, not a <c>Tracon.</c> prefix:
/// test assemblies and an application's own <c>Tracon.*</c> assemblies load
/// into the same process and are not part of it. The list is the library
/// packages without <c>Tracon.Client</c>, which talks to a server over HTTP and
/// may run against another release by design; the meta and template packages
/// carry no assembly, the <c>tracon</c> tool carries its own copy of the core,
/// and the source generator never loads at run time.
/// </para>
/// </remarks>
internal static class PackageFamilyAlignment
{
    /// <summary>The version an assembly reports when it cannot be read.</summary>
    internal const string UnknownVersion = "unknown";

    /// <summary>The assembly names that make up the Tracon package family, in ordinal order.</summary>
    internal static IReadOnlyList<string> FamilyAssemblyNames { get; } =
    [
        "Tracon.Abstractions",
        "Tracon.Anthropic",
        "Tracon.AspNetCore",
        "Tracon.Azure",
        "Tracon.Core",
        "Tracon.Google",
        "Tracon.Mcp",
        "Tracon.OpenAI",
        "Tracon.PostgreSql",
        "Tracon.SqlServer",
        "Tracon.Sqlite",
        "Tracon.Testing",
        "Tracon.Testing.Contracts.Xunit",
        "Tracon.UI",
        "Tracon.Voice",
        "Tracon.Workflows",
    ];

    private static readonly HashSet<string> FamilyNames = new(FamilyAssemblyNames, StringComparer.Ordinal);

    /// <summary>Reads the name and version of every family assembly in <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">The loaded assemblies; anything outside the family is ignored.</param>
    /// <param name="readVersion">Reads one assembly's version.</param>
    /// <returns>One entry per family assembly, in the order given.</returns>
    /// <remarks>
    /// A version that cannot be read is <see cref="UnknownVersion"/>, which
    /// <see cref="Describe"/> never treats as aligned. The reader runs in its own
    /// call so that an exception raised while it is compiled - a sibling that no
    /// longer has the member it calls - is caught here and not by the host.
    /// </remarks>
    internal static IReadOnlyList<LoadedFamilyAssembly> ReadLoaded(IEnumerable<Assembly> assemblies, Func<Assembly, string> readVersion)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(readVersion);

        var loaded = new List<LoadedFamilyAssembly>();

        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name;

            if (name is null || !FamilyNames.Contains(name))
            {
                continue;
            }

            loaded.Add(new LoadedFamilyAssembly(name, ReadVersionOrUnknown(assembly, readVersion)));
        }

        return loaded;
    }

    /// <summary>Describes a family that is not aligned.</summary>
    /// <param name="loaded">The family assemblies loaded into the process.</param>
    /// <returns>
    /// <see langword="null"/> when every assembly reports the same, known version;
    /// otherwise the error text, one assembly and version per line, ordered by name.
    /// </returns>
    internal static string? Describe(IEnumerable<LoadedFamilyAssembly> loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var distinct = loaded
            .Distinct()
            .OrderBy(assembly => assembly.Name, StringComparer.Ordinal)
            .ThenBy(assembly => assembly.Version, StringComparer.Ordinal)
            .ToList();

        var versions = distinct.Select(assembly => assembly.Version).Distinct(StringComparer.Ordinal).Count();
        var unknown = distinct.Exists(assembly => string.Equals(assembly.Version, UnknownVersion, StringComparison.Ordinal));

        if (versions <= 1 && !unknown)
        {
            return null;
        }

        var text = new StringBuilder()
            .AppendLine("Tracon packages from more than one release are loaded into this process. Every Tracon package ships on one version line, and these assemblies disagree:");

        foreach (var assembly in distinct)
        {
            text.Append(CultureInfo.InvariantCulture, $"    {assembly.Name} {assembly.Version}").AppendLine();
        }

        return text
            .Append("Reference every Tracon package at the same version, then restore and build again.")
            .ToString();
    }

    private static string ReadVersionOrUnknown(Assembly assembly, Func<Assembly, string> readVersion)
    {
        try
        {
            var version = readVersion(assembly);

            return string.IsNullOrWhiteSpace(version) ? UnknownVersion : version;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Fail closed: a version nobody can read never counts as aligned. The
            // likely cause is a sibling from another release, which is exactly
            // the graph this check exists to stop.
            return UnknownVersion;
        }
    }
}

/// <summary>One Tracon package assembly loaded into the process.</summary>
/// <param name="Name">The assembly name.</param>
/// <param name="Version">The informational version without build metadata, or <see cref="PackageFamilyAlignment.UnknownVersion"/>.</param>
internal sealed record LoadedFamilyAssembly(string Name, string Version);
