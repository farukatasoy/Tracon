using System.Globalization;

namespace AgentPrism;

/// <summary>
/// Bes alanli standart cron alt kumesinin (<c>dakika saat ayin-gunu ay
/// haftanin-gunu</c>) elle yazilmis ayristiricisi.
/// </summary>
/// <remarks>
/// <para>
/// Paket alinmadi: <c>Cronos</c> veya <c>NCrontab</c> kucuk paketlerdir ama
/// K-007 geregi her yeni bagimlilik tuketiciye gecer. Bes alanli ayristirici
/// ~150 satirdir ve testi kolaydir. Gerekce: docs/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md,
/// bolum 17.5.
/// </para>
/// <para>
/// Desteklenen sozdizimi: <c>*</c>, sabit deger, <c>N-M</c> araligi,
/// <c>deger1,deger2,...</c> listesi ve <c>/adim</c> (herhangi birinin
/// sonuna eklenebilir). Saniye alani, <c>L</c>, <c>W</c>, <c>#</c> gibi
/// vixie-cron uzantilari <strong>desteklenmez</strong> ve ayristirmada
/// <see cref="FormatException"/> ile reddedilir.
/// </para>
/// <para>
/// Haftanin gunu <em>ve</em> ayin gunu ikisi de kisitlanmissa (ikisi de
/// <c>*</c> degilse) standart cron kurali uygulanir: eslesme <strong>ya
/// biri ya digeri</strong> tuttugunda olusur (OR), her ikisi birden degil.
/// </para>
/// </remarks>
public sealed class CronExpression
{
    private readonly bool[] _minute;
    private readonly bool[] _hour;
    private readonly bool[] _dayOfMonth;
    private readonly bool[] _month;
    private readonly bool[] _dayOfWeek;
    private readonly bool _dayOfMonthRestricted;
    private readonly bool _dayOfWeekRestricted;

    private CronExpression(
        bool[] minute,
        bool[] hour,
        bool[] dayOfMonth,
        bool[] month,
        bool[] dayOfWeek,
        bool dayOfMonthRestricted,
        bool dayOfWeekRestricted)
    {
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
        _dayOfMonthRestricted = dayOfMonthRestricted;
        _dayOfWeekRestricted = dayOfWeekRestricted;
    }

    /// <summary>Bir cron ifadesini ayristirir.</summary>
    /// <param name="expression">Bes alanli cron ifadesi.</param>
    /// <returns>Ayristirilmis ifade.</returns>
    /// <exception cref="ArgumentException"><paramref name="expression"/> bos veya bosluktan ibaretse.</exception>
    /// <exception cref="FormatException">Ifade bes alandan olusmuyorsa veya desteklenmeyen bir sozdizimi iceriyorsa.</exception>
    public static CronExpression Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length != 5)
        {
            throw new FormatException(
                $"Cron ifadesi tam olarak 5 alan icermelidir (dakika saat ayin-gunu ay haftanin-gunu). " +
                $"Gelen alan sayisi: {fields.Length}. Saniye alani desteklenmez.");
        }

        var minute = ParseField(fields[0], 0, 59, "dakika", out _);
        var hour = ParseField(fields[1], 0, 23, "saat", out _);
        var dayOfMonth = ParseField(fields[2], 1, 31, "ayin-gunu", out var dayOfMonthRestricted);
        var month = ParseField(fields[3], 1, 12, "ay", out _);
        var dayOfWeek = ParseField(fields[4], 0, 6, "haftanin-gunu", out var dayOfWeekRestricted);

        return new CronExpression(minute, hour, dayOfMonth, month, dayOfWeek, dayOfMonthRestricted, dayOfWeekRestricted);
    }

    /// <summary>Bir cron ifadesini ayristirmayi dener.</summary>
    /// <param name="expression">Bes alanli cron ifadesi.</param>
    /// <param name="result">Basarili olursa ayristirilmis ifade.</param>
    /// <returns>Ayristirma basariliysa <see langword="true"/>.</returns>
    public static bool TryParse(string expression, out CronExpression? result)
    {
        try
        {
            result = Parse(expression);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Verilen andan sonraki, ifadeyle eslesen ilk zamani hesaplar.
    /// </summary>
    /// <param name="afterUtc">Aramanin baslayacagi an (UTC, haric).</param>
    /// <param name="timeZone">Ifadenin yorumlanacagi saat dilimi.</param>
    /// <returns>Bir sonraki calisma zamani (UTC).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> <see langword="null"/> ise.</exception>
    /// <exception cref="InvalidOperationException">4 yil icinde eslesen bir zaman bulunamazsa.</exception>
    /// <remarks>
    /// Yaz saati ileri atladiginda bazi yerel dakikalar hic yasanmaz; bu
    /// dakikalar sessizce atlanir. Saat geri alindiginda bir dakika iki kez
    /// yasanir; <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>
    /// varsayilan olarak ilk (erken) gerceklesmeyi secer.
    /// </remarks>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localAfter = TimeZoneInfo.ConvertTime(afterUtc, timeZone);
        var candidate = new DateTime(
            localAfter.Year,
            localAfter.Month,
            localAfter.Day,
            localAfter.Hour,
            localAfter.Minute,
            0,
            DateTimeKind.Unspecified).AddMinutes(1);

        var limit = candidate.AddYears(4);

        while (candidate <= limit)
        {
            if (Matches(candidate))
            {
                DateTime utc;

                try
                {
                    utc = TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);
                }
                catch (ArgumentException)
                {
                    // Yaz saati ileri atlamasi: bu yerel dakika hic yasanmadi.
                    candidate = candidate.AddMinutes(1);
                    continue;
                }

                return new DateTimeOffset(utc, TimeSpan.Zero);
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException(
            "Cron ifadesi icin 4 yil icinde eslesen bir sonraki calisma zamani bulunamadi.");
    }

    private bool Matches(DateTime local)
    {
        if (!_minute[local.Minute])
        {
            return false;
        }

        if (!_hour[local.Hour])
        {
            return false;
        }

        if (!_month[local.Month - 1])
        {
            return false;
        }

        var domMatch = _dayOfMonth[local.Day - 1];
        var dowMatch = _dayOfWeek[(int)local.DayOfWeek];

        // Standart cron kurali: her ikisi de kisitlanmissa OR, aksi halde AND
        // (kisitlanmamis alan zaten her zaman "true" doner, bu yuzden AND ile
        // ayni sonucu verir; kural yalnizca ikisi birden kisitliyken farklilasir).
        return _dayOfMonthRestricted && _dayOfWeekRestricted
            ? domMatch || dowMatch
            : domMatch && dowMatch;
    }

    private static bool[] ParseField(string field, int min, int max, string fieldName, out bool restricted)
    {
        restricted = !string.Equals(field, "*", StringComparison.Ordinal);

        var allowed = new bool[max - min + 1];

        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = part;
            var step = 1;
            var slashIndex = segment.IndexOf('/');

            if (slashIndex >= 0)
            {
                var stepText = segment[(slashIndex + 1)..];

                if (!int.TryParse(stepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step <= 0)
                {
                    throw new FormatException($"'{part}' gecersiz bir adim degeri tasiyor ({fieldName} alani).");
                }

                segment = segment[..slashIndex];
            }

            int rangeStart, rangeEnd;

            if (string.Equals(segment, "*", StringComparison.Ordinal))
            {
                rangeStart = min;
                rangeEnd = max;
            }
            else
            {
                var dashIndex = segment.IndexOf('-');

                if (dashIndex >= 0)
                {
                    if (!int.TryParse(segment[..dashIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart)
                        || !int.TryParse(segment[(dashIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeEnd))
                    {
                        throw new FormatException($"'{part}' gecerli bir araligi degil ({fieldName} alani).");
                    }
                }
                else
                {
                    if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart))
                    {
                        throw new FormatException(
                            $"'{part}' gecerli bir sayi degil ({fieldName} alani). " +
                            "Saniye alani ve L/W/# uzantilari desteklenmez.");
                    }

                    rangeEnd = rangeStart;
                }
            }

            if (rangeStart < min || rangeEnd > max || rangeStart > rangeEnd)
            {
                throw new FormatException(
                    $"'{part}' degeri {fieldName} alaninin gecerli araliginin ({min}-{max}) disinda.");
            }

            for (var value = rangeStart; value <= rangeEnd; value += step)
            {
                allowed[value - min] = true;
            }
        }

        return allowed;
    }
}
