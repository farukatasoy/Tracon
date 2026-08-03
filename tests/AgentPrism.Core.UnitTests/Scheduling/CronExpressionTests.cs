namespace AgentPrism.Core.UnitTests.Scheduling;

public sealed class CronExpressionTests
{
    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 3 * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 1 1 *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("0,30 8-17 * * *")]
    public void Gecerli_ifadeler_ayristirilir(string expression)
    {
        CronExpression.TryParse(expression, out var result).ShouldBeTrue();
        result.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("* * * *")] // dort alan
    [InlineData("* * * * * *")] // alti alan (saniye)
    [InlineData("60 * * * *")] // dakika araligi disi
    [InlineData("* 24 * * *")] // saat araligi disi
    [InlineData("* * 32 * *")] // ayin gunu araligi disi
    [InlineData("* * L * *")] // vixie-cron uzantisi
    [InlineData("* * * * ?")] // desteklenmeyen sozdizimi
    public void Gecersiz_ifadeler_reddedilir(string expression)
    {
        CronExpression.TryParse(expression, out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_gecersiz_ifadede_FormatException_firlatir()
    {
        Should.Throw<FormatException>(() => CronExpression.Parse("* * * * * *"));
    }

    [Fact]
    public void Gunluk_saat_bir_sonraki_gune_atlar()
    {
        var cron = CronExpression.Parse("30 3 * * *");
        var after = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 2, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Adim_degeri_belirtilen_araliklarla_eslesir()
    {
        var cron = CronExpression.Parse("*/15 * * * *");
        var after = new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 1, 10, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Haftanin_gunu_dogru_gune_atlar()
    {
        // 2026-01-01 Persembe. Bir sonraki Pazartesi (1) 2026-01-05'tir.
        var cron = CronExpression.Parse("0 9 * * 1");
        var after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Ayin_gunu_ve_haftanin_gunu_ikisi_de_kisitliyken_OR_ile_eslesir()
    {
        // Standart cron kurali: her ikisi kisitliyken eslesme "ya biri ya digeri"dir.
        // Ayin 15'i VEYA Pazartesi (1) her ay eslesir; hangisi once gelirse.
        var cron = CronExpression.Parse("0 0 15 * 1");

        // 2026-01-01 Persembe -> ilk eslesme Pazartesi 2026-01-05 (15'inden once).
        var next = cron.GetNextOccurrence(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Yaz_saati_ileri_atlamasinda_gecersiz_yerel_zaman_atlanir()
    {
        // ABD'de saat 2024-03-10'da 02:00'dan 03:00'a atlar (2007'den beri
        // yasayla sabit kural); 02:30 o gun hic yasanmaz. Sonraki eslesme bir
        // gun sonraya kaymalidir.
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var cron = CronExpression.Parse("30 2 * * *");

        var after = new DateTimeOffset(2024, 3, 9, 12, 0, 0, TimeSpan.Zero);
        var next = cron.GetNextOccurrence(after, timeZone);
        var local = TimeZoneInfo.ConvertTime(next, timeZone);

        local.Date.ShouldBe(new DateTime(2024, 3, 11));
        local.TimeOfDay.ShouldBe(TimeSpan.FromMinutes(150));
    }
}
