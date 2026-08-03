namespace AgentPrism;

/// <summary>
/// Bir anin hangi kota donemine dustugunu ve o donemin ne zaman sifirlanacagini
/// hesaplar.
/// </summary>
/// <remarks>
/// <para>
/// Saf mantiktir: durum tutmaz, saat okumaz. Boylece birim testi gercek zamana
/// bagli olmadan gun ve ay sinirlarini, yaz saati gecisini ve ay sonlarini
/// sinayabilir.
/// </para>
/// <para>
/// 🚨 Donem siniri <strong>yerel</strong> saat diliminde hesaplanir, UTC'de
/// degil. "Gunluk kota" diyen bir yonetici kendi is gununu kasteder; UTC'ye
/// gore sifirlamak <c>UTC+03</c> bir kiracinin sayacini ogleden once uc saat
/// erken sifirlardi.
/// </para>
/// </remarks>
public static class QuotaPeriodCalculator
{
    /// <summary>Bir anin dustugu donemin ilk gununu bulur.</summary>
    /// <param name="instant">An (UTC veya baska bir ofset).</param>
    /// <param name="period">Donem araligi.</param>
    /// <param name="timeZone">Donem sinirinin hesaplanacagi saat dilimi.</param>
    /// <returns>Donemin ilk gunu, yerel takvimde.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> <see langword="null"/> ise.</exception>
    public static DateOnly GetPeriodStart(DateTimeOffset instant, QuotaPeriod period, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);

        return period switch
        {
            QuotaPeriod.Monthly => new DateOnly(date.Year, date.Month, 1),
            _ => date,
        };
    }

    /// <summary>Bir anin dustugu donemin ne zaman sifirlanacagini bulur.</summary>
    /// <param name="instant">An.</param>
    /// <param name="period">Donem araligi.</param>
    /// <param name="timeZone">Donem sinirinin hesaplanacagi saat dilimi.</param>
    /// <returns>Bir sonraki donemin basladigi an (UTC).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Donen deger istemciye <c>Retry-After</c> ve <c>ProblemDetails</c> icinde
    /// "ne zaman sifirlanir" olarak yansir.
    /// </remarks>
    public static DateTimeOffset GetPeriodEnd(DateTimeOffset instant, QuotaPeriod period, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var start = GetPeriodStart(instant, period, timeZone);

        var nextStart = period switch
        {
            QuotaPeriod.Monthly => start.AddMonths(1),
            _ => start.AddDays(1),
        };

        return ToUtcInstant(nextStart, timeZone);
    }

    /// <summary>Bir donem baslangicini o saat diliminde gece yarisina cevirir.</summary>
    /// <param name="date">Donemin ilk gunu (yerel takvim).</param>
    /// <param name="timeZone">Saat dilimi.</param>
    /// <returns>Yerel gece yarisinin UTC karsiligi.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// 🚨 Yaz saati gecisinde yerel gece yarisi <em>var olmayabilir</em> (ileri
    /// atlama) veya <em>iki kez</em> gecerli olabilir (geri alma). Var olmayan
    /// bir saat, gecisin hemen sonrasina tasinir; belirsiz bir saatte
    /// <strong>daha erken</strong> olan ofset secilir. Ikisi de sessizce
    /// yanlis sonuc uretmek yerine tanimli davranistir.
    /// </remarks>
    public static DateTimeOffset ToUtcInstant(DateOnly date, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(midnight))
        {
            // Gece yarisi ileri atlamayla yutuldu: gecerli ilk ani bul.
            // Gecis en fazla birkac saat surer; dakika dakika ilerlemek
            // butun saat dilimi kurallarinda guvenlidir.
            for (var minutes = 1; minutes <= 24 * 60; minutes++)
            {
                var candidate = midnight.AddMinutes(minutes);

                if (!timeZone.IsInvalidTime(candidate))
                {
                    midnight = candidate;
                    break;
                }
            }
        }

        // Belirsiz (iki kez yasanan) bir saatte GetUtcOffset daha erken olan
        // standart-disi ofseti dondurur; donem sinirinin erken baslamasi gec
        // baslamasina yeglenir — kota bir dakika erken sifirlanabilir, ama
        // hicbir tuketim yanlis doneme yazilmaz.
        var offset = timeZone.GetUtcOffset(midnight);

        return new DateTimeOffset(midnight, offset).ToUniversalTime();
    }

    /// <summary>Bir kural kumesinin dokundugu tum donemlerin baslangicini hesaplar.</summary>
    /// <param name="instant">An.</param>
    /// <param name="timeZone">Saat dilimi.</param>
    /// <returns>Her donem araligi icin o donemin ilk gunu.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeZone"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Tuketim her iki donem sayacina da yazilir: bir kiraci ayni anda hem
    /// gunluk hem aylik kota tanimlayabilir ve ikisi bagimsiz sayilir.
    /// </remarks>
    public static IReadOnlyDictionary<QuotaPeriod, DateOnly> GetAllPeriodStarts(
        DateTimeOffset instant,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return new Dictionary<QuotaPeriod, DateOnly>
        {
            [QuotaPeriod.Daily] = GetPeriodStart(instant, QuotaPeriod.Daily, timeZone),
            [QuotaPeriod.Monthly] = GetPeriodStart(instant, QuotaPeriod.Monthly, timeZone),
        };
    }
}
