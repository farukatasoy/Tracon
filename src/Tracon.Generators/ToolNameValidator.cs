namespace Tracon.Generators;

/// <summary>Validates tool name rules (APG0002).</summary>
internal static class ToolNameValidator
{
    private const int MaxLength = 64;

    /// <summary>
    /// Determines whether a tool name is valid. The rule matches the OpenAI function
    /// call schema: one to 64 characters, with only letters, digits, underscore, or hyphen.
    /// </summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            var isAllowed =
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '_' ||
                c == '-';

            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
