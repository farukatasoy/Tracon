namespace AgentPrism;

/// <summary>
/// Bir cagri agacinin tamami boyunca paylasilan calistirma butcesi.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bu tip bilerek bir <c>class</c>'tir, <c>record</c> degil.</strong>
/// Butce paylasilan degisken durumdur: agactaki her calistirma <em>ayni</em>
/// ornegi kullanir. Bir <c>record</c> kopyalanmaya davet eder; kopyalanan
/// butce her dala kendi sinirini verir ve sinir anlamini yitirir.
/// </para>
/// <para>
/// Sayaclar kilitsiz artar (<see cref="Interlocked"/>). Alt calistirmalar
/// es zamanli baslar: Microsoft Agent Framework'un arka plan agent'lari
/// bloke etmeden calisir, dolayisiyla ayni butce birden cok is parcaciginda
/// okunup yazilir.
/// </para>
/// <para>
/// Butce <strong>yeni</strong> alt calistirmalari engeller; suren bir
/// calistirmayi kesmez. Yarim kesilen bir alt calistirma modele eksik bir
/// baglam birakir ve kok calistirmayi da bozardi.
/// </para>
/// </remarks>
public sealed class AgentRunBudget
{
    private long _consumedTokens;
    private int _startedRuns;

    /// <summary>
    /// Agac boyunca harcanabilecek en fazla token. <see langword="null"/> ise
    /// token sinirlamasi yoktur.
    /// </summary>
    public long? MaxTotalTokens { get; init; }

    /// <summary>
    /// Baslatilabilecek en fazla <em>alt</em> calistirma sayisi. Kok calistirma
    /// bu sayiya dahil degildir. <see langword="null"/> ise sayi sinirlamasi yoktur.
    /// </summary>
    public int? MaxTotalRuns { get; init; }

    /// <summary>
    /// Izin verilen en buyuk cagri derinligi. Kok calistirma 0'dir, dolayisiyla
    /// varsayilan deger uc katmanli bir agaca izin verir.
    /// </summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>Agac boyunca simdiye kadar harcanan token sayisi.</summary>
    public long ConsumedTokens => Interlocked.Read(ref _consumedTokens);

    /// <summary>Simdiye kadar baslatilmis alt calistirma sayisi.</summary>
    public int StartedRuns => Volatile.Read(ref _startedRuns);

    /// <summary>Token siniri asilmis mi.</summary>
    public bool IsTokenBudgetExhausted
        => MaxTotalTokens is { } max && ConsumedTokens >= max;

    /// <summary>
    /// Yeni bir alt calistirma icin butcede yer ayirir.
    /// </summary>
    /// <returns>
    /// Yer ayrilabildiyse <see langword="true"/>; token veya sayi siniri
    /// asildiysa <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Sayac yalnizca yer ayrildiginda artar. Basarisiz bir deneme sayaci
    /// artirsaydi, sinira ulasmis bir agacta her yeni deneme sayiyi buyutur ve
    /// arayuzde gercekte baslamamis calistirmalar gorunurdu.
    /// </remarks>
    public bool TryReserveRun()
    {
        if (IsTokenBudgetExhausted)
        {
            return false;
        }

        if (MaxTotalRuns is not { } maxRuns)
        {
            Interlocked.Increment(ref _startedRuns);
            return true;
        }

        // CAS dongusu: es zamanli iki alt cagri sinirin son yerini ayni anda
        // istediginde yalnizca biri kazanmalidir.
        var current = Volatile.Read(ref _startedRuns);

        while (current < maxRuns)
        {
            var previous = Interlocked.CompareExchange(ref _startedRuns, current + 1, current);

            if (previous == current)
            {
                return true;
            }

            current = previous;
        }

        return false;
    }

    /// <summary>Harcanan token sayisini butceye isler.</summary>
    /// <param name="tokens">Eklenecek token sayisi. Negatif deger yok sayilir.</param>
    public void RecordUsage(long tokens)
    {
        if (tokens <= 0)
        {
            return;
        }

        Interlocked.Add(ref _consumedTokens, tokens);
    }

    /// <summary>Sinir asimini anlatan, kullaniciya gosterilebilir bir metin uretir.</summary>
    /// <returns>Hangi sinirin asildigini soyleyen metin.</returns>
    /// <remarks>
    /// Metin modele tool sonucu olarak doner. "Butce bitti" demek yetmez;
    /// hangi sinirin asildigi yazilmazsa kullanici hangi ayari yukseltmesi
    /// gerektigini goremez.
    /// </remarks>
    public string DescribeExhaustion()
        => IsTokenBudgetExhausted
            ? $"Calistirma agacinin token butcesi doldu ({ConsumedTokens}/{MaxTotalTokens}). " +
              "Yeni alt calistirma baslatilamaz."
            : $"Calistirma agacinin alt calistirma siniri doldu ({StartedRuns}/{MaxTotalRuns}). " +
              "Yeni alt calistirma baslatilamaz.";
}
