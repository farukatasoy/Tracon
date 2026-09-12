using System.Reflection;

namespace Tracon;

/// <summary>
/// Reads the package version an assembly was built as, in the form that is
/// stamped onto persisted state and reported back to an operator.
/// </summary>
/// <remarks>
/// One implementation, three callers: the session writer, the workflow
/// checkpoint writer, and the state preflight all have to name the SAME
/// version in the SAME shape, or a mismatch report compares two strings that
/// were never comparable.
/// </remarks>
internal static class AssemblyVersionText
{
    /// <summary>Reads <paramref name="assembly"/>'s informational version, without the build metadata suffix.</summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <returns>
    /// The informational version up to the <c>+</c> separator; the assembly
    /// version when there is no informational version; <c>unknown</c> when
    /// neither is present.
    /// </returns>
    internal static string Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? informational : informational[..plus];
    }
}
