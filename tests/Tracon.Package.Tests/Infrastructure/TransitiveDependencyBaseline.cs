namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// Reads <c>Baselines/transitive-dependencies.txt</c>: one <c>#</c>-headed
/// section per consumer shape, one package ID per line.
/// </summary>
internal static class TransitiveDependencyBaseline
{
    private static readonly string Path = System.IO.Path.Combine(
        AppContext.BaseDirectory, "Baselines", "transitive-dependencies.txt");

    /// <summary>Reads the baseline set for one consumer shape.</summary>
    /// <param name="shape">The package ID that names the shape's section header (e.g. <c>Tracon.Core</c>).</param>
    public static IReadOnlySet<string> Read(string shape)
    {
        var lines = File.ReadAllLines(Path);
        var header = $"# {shape}";
        var result = new HashSet<string>(StringComparer.Ordinal);
        var inSection = false;

        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                inSection = string.Equals(line, header, StringComparison.Ordinal);
                continue;
            }

            if (inSection && !string.IsNullOrWhiteSpace(line))
            {
                result.Add(line.Trim());
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException($"No baseline section '{header}' was found in '{Path}'.");
        }

        return result;
    }
}
