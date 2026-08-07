namespace AgentPrism;

/// <summary>
/// Desen eslesmelerini dogrulayan kontrol basamagi algoritmalari.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Bu sinif <see cref="PatternContentGuard"/>'in kullanilabilir olmasinin
/// sartidir. Yalniz <c>\d{16}</c> eslesmesi her siparis numarasini, yalniz
/// <c>\d{11}</c> eslesmesi her takip numarasini maskeler; boyle bir guard ilk
/// gunde kapatilir.
/// </para>
/// <para>
/// Metotlar <see cref="ReadOnlySpan{T}"/> alir ve hicbir tahsis yapmaz: sicak
/// yolda her eslesme icin cagrilirlar.
/// </para>
/// </remarks>
internal enum CheckDigitKind
{
    /// <summary>Dogrulama yapilmaz; desen eslesmesi tek basina yeterlidir.</summary>
    None = 0,

    /// <summary>Luhn kontrol basamagi (kredi karti).</summary>
    Luhn = 1,

    /// <summary>TC kimlik numarasi kontrol basamaklari.</summary>
    TurkishNationalId = 2,
}

internal static class CheckDigits
{
    /// <summary>
    /// Luhn kontrol basamagi dogrulamasi (kredi karti, IMEI).
    /// </summary>
    /// <param name="digits">
    /// Yalnizca rakam ve ayirici (bosluk, tire) icerebilen aday dizi.
    /// </param>
    /// <returns>Kontrol basamagi tutuyorsa <see langword="true"/>.</returns>
    public static bool IsValidLuhn(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var count = 0;

        // Sagdan sola yurunur: ciftleme sirasi son basamaktan belirlenir.
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var character = digits[index];

            if (!char.IsAsciiDigit(character))
            {
                continue;
            }

            var value = character - '0';

            if (count % 2 == 1)
            {
                value *= 2;

                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            count++;
        }

        return count >= 12 && sum % 10 == 0;
    }

    /// <summary>
    /// TC kimlik numarasi kontrol basamagi dogrulamasi.
    /// </summary>
    /// <param name="digits">On bir haneli aday dizi.</param>
    /// <returns>Iki kontrol basamagi da tutuyorsa <see langword="true"/>.</returns>
    /// <remarks>
    /// Kural: 10. basamak = ((1., 3., 5., 7., 9. toplami) × 7 − (2., 4., 6., 8.
    /// toplami)) mod 10; 11. basamak = ilk on basamagin toplami mod 10. Ilk
    /// basamak sifir olamaz.
    /// </remarks>
    public static bool IsValidTurkishNationalId(ReadOnlySpan<char> digits)
    {
        if (digits.Length != 11 || digits[0] == '0')
        {
            return false;
        }

        var oddSum = 0;
        var evenSum = 0;
        var firstTenSum = 0;

        for (var index = 0; index < 10; index++)
        {
            if (!char.IsAsciiDigit(digits[index]))
            {
                return false;
            }

            var value = digits[index] - '0';
            firstTenSum += value;

            // 🚨 Yalnizca ILK DOKUZ basamak tek/cift toplamlarina girer. Onuncu
            // basamak (index 9) kontrol basamagidir ve kendi formulunun girdisi
            // olamaz; toplamlara katmak her gecerli numarayi gecersiz gosterir.
            if (index >= 9)
            {
                continue;
            }

            if (index % 2 == 0)
            {
                oddSum += value;
            }
            else
            {
                evenSum += value;
            }
        }

        if (!char.IsAsciiDigit(digits[10]))
        {
            return false;
        }

        // Cikarma negatif olabilir; C#'ta negatif mod negatif doner, bu yuzden
        // 10 eklenip yeniden mod alinir.
        var tenth = (((oddSum * 7) - evenSum) % 10 + 10) % 10;

        if (tenth != digits[9] - '0')
        {
            return false;
        }

        return firstTenSum % 10 == digits[10] - '0';
    }
}
