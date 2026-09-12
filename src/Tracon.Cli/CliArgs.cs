namespace Tracon.Cli;

/// <summary>Reads <c>--name value</c> style arguments by hand — three commands do not need a full parser library.</summary>
internal static class CliArgs
{
    /// <summary>Returns the value following <paramref name="name"/>, or <see langword="null"/> if absent.</summary>
    public static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>Returns whether a bare flag (no value) is present.</summary>
    public static bool HasFlag(IReadOnlyList<string> args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.Ordinal));

    /// <summary>
    /// Returns <paramref name="name"/>'s value, falling back to <paramref name="environmentVariable"/>
    /// when the flag is absent. Throws <see cref="CliArgumentException"/> when neither is set.
    /// </summary>
    /// <remarks>
    /// A secret (connection string, token) is read ONLY from a command line
    /// argument or an environment variable - never from a configuration file.
    /// </remarks>
    public static string RequireOption(IReadOnlyList<string> args, string name, string? environmentVariable = null)
    {
        var value = GetOption(args, name);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (environmentVariable is not null)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                return fromEnvironment;
            }
        }

        var suffix = environmentVariable is null ? "." : $", or set the {environmentVariable} environment variable.";
        throw new CliArgumentException($"Missing required option '{name}'{suffix}");
    }
}
