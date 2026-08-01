namespace AgentPrism;

/// <summary>
/// PostgreSQL tanimlayicilarini dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Sema adi yapilandirmadan gelir ve SQL metnine <em>dogrudan</em> yerlestirilir —
/// tanimlayicilar parametre olarak gonderilemez. Bu yuzden ad, SQL metnine
/// girmeden once burada kati bicimde dogrulanir: yalnizca kucuk harf, rakam ve
/// alt cizgi kabul edilir. Bu, sema adi uzerinden SQL enjeksiyonunu imkansiz kilar.
/// </para>
/// <para>
/// Yalnizca kucuk harf kabul edilmesinin sebebi PostgreSQL'in tirnaksiz
/// tanimlayicilari kucuk harfe cevirmesidir; buyuk harf iceren bir ad, yazildigi
/// gibi geri okunamaz.
/// </para>
/// </remarks>
internal static class SqlIdentifier
{
    /// <summary>PostgreSQL tanimlayici uzunluk siniri (<c>NAMEDATALEN - 1</c>).</summary>
    private const int MaxLength = 63;

    /// <summary>Deger tirnaksiz kullanilabilecek gecerli bir tanimlayici mi.</summary>
    /// <param name="value">Denetlenecek ad.</param>
    /// <returns>Gecerliyse <see langword="true"/>.</returns>
    public static bool IsValidUnquoted(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        var first = value[0];

        if (!IsLowerLetter(first) && first != '_')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsLowerLetter(character) && !IsDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gecerli bir sema adi dondurur; degilse hata verir.</summary>
    /// <param name="value">Denetlenecek sema adi.</param>
    /// <returns>Dogrulanmis sema adi.</returns>
    /// <exception cref="AgentPrismException">Ad gecerli bir tanimlayici degilse.</exception>
    public static string RequireSchemaName(string? value)
    {
        if (!IsValidUnquoted(value))
        {
            throw new AgentPrismException(
                $"'{value}' gecerli bir PostgreSQL sema adi degil. Kucuk harf veya alt cizgi ile baslamali; " +
                $"kucuk harf, rakam ve alt cizgi icermeli; en cok {MaxLength} karakter olmalidir.");
        }

        return value!;
    }

    private static bool IsLowerLetter(char character) => character is >= 'a' and <= 'z';

    private static bool IsDigit(char character) => character is >= '0' and <= '9';
}
