namespace AgentPrism.Generators;

/// <summary>Tool adi kurallarini dogrular (APG0002).</summary>
internal static class ToolNameValidator
{
    private const int MaxLength = 64;

    /// <summary>
    /// Bir tool adinin gecerli olup olmadigini denetler. Kural OpenAI fonksiyon
    /// cagrisi semasiyla ayni: 1-64 karakter, yalnizca harf, rakam, alt cizgi
    /// veya tire.
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
