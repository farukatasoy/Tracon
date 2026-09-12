namespace Tracon;

/// <summary>Shared, internal validation rules for registered tool names.</summary>
internal static class ToolNameRules
{
    internal const string Description = "Tool names must match [A-Za-z0-9_-]{1,64}.";

    internal static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
