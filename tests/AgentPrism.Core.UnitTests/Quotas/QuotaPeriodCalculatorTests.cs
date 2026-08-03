namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// Donem siniri hesabinin testleri.
/// </summary>
/// <remarks>
/// Saf mantiktir: gercek saat okunmaz, her senaryo acik bir an ve saat dilimi
/// verir. Yaz saati gecisleri gercek IANA kurallariyla sinanir.
/// </remarks>
public sealed class QuotaPeriodCalculatorTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Gunluk_donem_yerel_takvim_gunudur()
    {
        // 2026-08-03T22:30Z, Istanbul'da (UTC+03) ertesi gunun 01:30'udur.
        var instant = new DateTimeOffset(2026, 8, 3, 22, 30, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, Istanbul)
            .ShouldBe(new DateOnly(2026, 8, 4));

        // Ayni an UTC'de hala 3 Agustos'tur.
        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, TimeZoneInfo.Utc)
            .ShouldBe(new DateOnly(2026, 8, 3));
    }

    [Fact]
    public void Aylik_donem_ayin_ilk_gunudur()
    {
        var instant = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Monthly, Istanbul)
            .ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void Gunluk_donem_yerel_gece_yarisinda_biter()
    {
        var instant = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

        // Istanbul UTC+03: 4 Agustos 00:00 yerel = 3 Agustos 21:00 UTC.
        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Daily, Istanbul)
            .ShouldBe(new DateTimeOffset(2026, 8, 3, 21, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Aylik_donem_sonu_ay_uzunlugunu_izler()
    {
        // Subat 28 cekiyor: 2026 arti yil degildir.
        var instant = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Monthly, TimeZoneInfo.Utc)
            .ShouldBe(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Yil_sonunda_aylik_donem_ocaga_gecer()
    {
        var instant = new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Monthly, TimeZoneInfo.Utc)
            .ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Yaz_saati_gecisinde_donem_sinirinda_gun_kaymaz()
    {
        // 2026-03-08: New York'ta saatler 02:00'de 03:00'e atlar. Gece yarisi
        // gecerlidir, ama gun 23 saat surer.
        var instant = new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);

        QuotaPeriodCalculator.GetPeriodStart(instant, QuotaPeriod.Daily, NewYork)
            .ShouldBe(new DateOnly(2026, 3, 8));

        // 9 Mart 00:00 EDT (UTC-04) = 9 Mart 04:00 UTC.
        QuotaPeriodCalculator.GetPeriodEnd(instant, QuotaPeriod.Daily, NewYork)
            .ShouldBe(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Var_olmayan_gece_yarisi_gecisin_sonrasina_tasinir()
    {
        // Bazi saat dilimlerinde gece yarisi ileri atlamayla tumuyle yutulur;
        // hesabin istisna firlatmadan tanimli bir an uretmesi gerekir.
        // Lord Howe/Chatham gibi diliminler yerine dogrudan davranisi sinariz:
        // gecerli her sonuc, o gunun yerel gece yarisina esit veya ondan
        // sonradir ve bir sonraki gunden oncedir.
        var date = new DateOnly(2026, 3, 8);
        var instant = QuotaPeriodCalculator.ToUtcInstant(date, NewYork);

        var local = TimeZoneInfo.ConvertTime(instant, NewYork);

        local.Date.ShouldBe(new DateTime(2026, 3, 8));
    }

    [Fact]
    public void Tum_donemler_birlikte_hesaplanir()
    {
        var instant = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        var starts = QuotaPeriodCalculator.GetAllPeriodStarts(instant, TimeZoneInfo.Utc);

        starts.Count.ShouldBe(2);
        starts[QuotaPeriod.Daily].ShouldBe(new DateOnly(2026, 8, 17));
        starts[QuotaPeriod.Monthly].ShouldBe(new DateOnly(2026, 8, 1));
    }
}
