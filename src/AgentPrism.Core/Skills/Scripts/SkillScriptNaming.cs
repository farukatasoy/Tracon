namespace AgentPrism;

// 🚨 This guard exists because a stored script's name was never checked for path
// characters. The name travels from PUT /api/skills/{name} straight into
// Path.Combine, and .NET's Path.Combine returns the SECOND part unchanged when
// that part is rooted, and never resolves "..". A script named
// "/etc/cron.d/agentprism" was therefore written outside the scratch directory,
// executed from there, and survived the cleanup - which only deletes the scratch
// directory itself.
//
// K-087 kept the skill ROOT out of the interface's reach; this closes the other
// half, where the interface supplied the target PATH instead. The rule is
// enforced in TWO places on purpose: the save endpoint, so the caller gets a 400
// instead of a failed run, and the runner, because that is the boundary that
// actually writes the file.

/// <summary>Validates the file name a stored script is written under.</summary>
public static class SkillScriptNaming
{
    /// <summary>Returns the name when it is a plain file name.</summary>
    /// <param name="scriptName">The script name as stored.</param>
    /// <returns>The same name.</returns>
    /// <exception cref="AgentPrismException">The name is not a plain file name.</exception>
    public static string RequireSafeFileName(string scriptName)
    {
        if (!IsSafeFileName(scriptName))
        {
            throw new AgentPrismException(
                $"Script name '{scriptName}' is not a plain file name. A script name may not be empty, " +
                "and may not contain a directory separator, a path root, or '..'.");
        }

        return scriptName;
    }

    /// <summary>Reports whether the name is a plain file name.</summary>
    /// <param name="scriptName">The candidate name.</param>
    /// <returns><see langword="true"/> when the name is safe to combine with a directory.</returns>
    public static bool IsSafeFileName(string? scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName))
        {
            return false;
        }

        if (scriptName is "." or "..")
        {
            return false;
        }

        if (Path.IsPathRooted(scriptName))
        {
            return false;
        }

        // Both separators and the drive marker are rejected on every platform: a
        // name accepted on Linux must not become a traversal when the same record
        // is executed on Windows.
        if (scriptName.Contains('/', StringComparison.Ordinal) ||
            scriptName.Contains('\\', StringComparison.Ordinal) ||
            scriptName.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return scriptName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
