namespace AgentPrism;

/// <summary>AgentPrism'in urettigi tum hatalarin taban sinifi.</summary>
public class AgentPrismException : Exception
{
    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Calistirma kaydina yazilacak kararli hata tipi adi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Varsayilan olarak istisna tipinin tam adidir; boylece bugunku davranis
    /// degismez. Bir alt tip, kaydin makine tarafindan okunabilir olmasi gerektiginde
    /// bu uyeyi gecersiz kilar — ornek: <see cref="AgentPrismContentFilteredException"/>
    /// <c>content_filtered</c> yazar. Rapor ve uyari kurallari derleme adina degil,
    /// bu kararli ada dayanabilir.
    /// </para>
    /// <para>
    /// Deger <c>RunError.Type</c> alanina yazilir ve <strong>sir tasimaz</strong>.
    /// </para>
    /// </remarks>
    public virtual string ErrorType => GetType().FullName ?? GetType().Name;
}

/// <summary>
/// Model saglayicisi yaniti guvenlik/icerik filtresiyle kestiginde atilir.
/// </summary>
/// <remarks>
/// <para>
/// Filtrelenmis bir yanit cogu zaman <strong>bos</strong> gelir. Bunu "basarili ama
/// bos" saymak, hata ayiklamasi en zor durumu uretir: kullanici bos bir cevap gorur,
/// kayitta hicbir iz yoktur. AgentPrism bunu acik bir hata olarak kaydeder —
/// <c>RunError.Type</c> alani <c>content_filtered</c> olur.
/// </para>
/// <para>
/// Tespit <c>AgentPrism.Core</c> icindeki ortak bir <c>IChatClient</c> dekoratorunde
/// yapilir, saglayici paketlerinin icinde degil; boylece her saglayici ayni davranisi
/// alir. Gerekce: <c>docs/26-ANTHROPIC-VE-GEMINI.md</c>, bolum 26.4.
/// </para>
/// </remarks>
public sealed class AgentPrismContentFilteredException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string ContentFilteredErrorType = "content_filtered";

    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismContentFilteredException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismContentFilteredException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismContentFilteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Yaniti filtreleyen saglayicinin adi. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Saglayicinin bildirdigi bitis sebebi. Ornek: Anthropic <c>refusal</c>,
    /// Gemini <c>SAFETY</c>. <strong>Sir tasimaz.</strong>
    /// </summary>
    public string? FinishReason { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ContentFilteredErrorType;
}

/// <summary>
/// Icerik bir <see cref="IContentGuard"/> tarafindan engellendiginde atilir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentPrismContentFilteredException"/>'dan ayridir ve ayri kalmalidir:
/// o, <em>saglayicinin</em> yaniti kestigini bildirir; bu ise AgentPrism'in kendi
/// politikasinin icerigi gecirmedigini bildirir. Ikisini ayni kararli kimlige
/// yazmak, operatorun "model reddetti" ile "biz reddettik" arasindaki ayrimi
/// kaybetmesine yol acardi.
/// </para>
/// <para>
/// 🚨 Ne mesaj ne de alanlar <strong>engellenen icerigi tasir</strong>. Mesaj
/// istemciye <c>422</c> govdesinde donebilir; oraya hassas metin yazmak sorunu
/// yayardi.
/// </para>
/// </remarks>
public sealed class AgentPrismContentBlockedException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string ContentBlockedErrorType = "content_blocked";

    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismContentBlockedException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji. Engellenen metni TASIMAMALIDIR.</param>
    public AgentPrismContentBlockedException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji. Engellenen metni TASIMAMALIDIR.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismContentBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Karari veren guard'in adi.</summary>
    public string? GuardName { get; init; }

    /// <summary>Eslesen kuralin adi. Guard kural adi bildirmediyse <see langword="null"/>.</summary>
    public string? RuleName { get; init; }

    /// <summary>Denetimin yonu.</summary>
    public ContentGuardDirection Direction { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ContentBlockedErrorType;
}

/// <summary>
/// Bir agent tanimi calistirilabilir bir agent'a donusturulemedigi zaman atilir.
/// </summary>
/// <remarks>
/// En sik sebep: tanimda kodda kayitli olmayan bir tool adinin bulunmasi.
/// Bu durum sessizce atlanmaz — bilinmeyen bir tool ile calisan agent,
/// kullanicinin bekledigini yapmayan agent demektir.
/// </remarks>
public sealed class AgentPrismCompilationException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string CompilationFailedErrorType = "compilation_failed";

    /// <summary>Yeni bir derleme hatasi olusturur.</summary>
    public AgentPrismCompilationException()
    {
    }

    /// <summary>Yeni bir derleme hatasi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismCompilationException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir derleme hatasi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismCompilationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Derlenemeyen agent'in adi.</summary>
    public string? AgentName { get; init; }

    /// <inheritdoc />
    public override string ErrorType => CompilationFailedErrorType;
}

/// <summary>
/// Bir model saglayicisi devre kesici tarafindan gecici olarak kapatildiginda atilir.
/// </summary>
/// <remarks>
/// Devre <c>Open</c> durumdayken atilir; saglayiciya <strong>hicbir istek gitmez</strong>.
/// Ardisik hata sayisi <c>FailureThreshold</c>'u astiginda devre acilir ve
/// <c>BreakDuration</c> sonunda tek bir deneme icin yari-acik duruma gecer.
/// Gerekce: <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3.
/// </remarks>
public sealed class AgentPrismProviderUnavailableException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string ProviderUnavailableErrorType = "provider_unavailable";

    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismProviderUnavailableException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismProviderUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Devresi acik olan saglayicinin adi.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Devrenin yari-acik duruma gecip yeniden denenecegi zaman.</summary>
    public DateTimeOffset? RetryAfter { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ProviderUnavailableErrorType;
}

/// <summary>
/// Ayni YENI oturum kimligine eszamanli iki ilk istek geldiginde, kaybeden
/// istek icin atilir.
/// </summary>
/// <remarks>
/// <para>
/// HATA-004: <c>AgentSessionManager.GetOrCreateSessionAsync</c> yeni bir oturum
/// olustururken <c>ISessionStore.TryCreateAsync</c> ile atomik bir kayit dener.
/// Ayni kimlikle eszamanli ikinci bir istek bu denemede kaybederse, kazananin
/// konusma gecmisi saglayicisinin (ChatHistoryProvider) konusma kimligini HENUZ
/// uretmemis olabilecegini bilemez — o kimlik yalniz kazananin ILK turu
/// calisirken uretilir. Kaybeden yine de kendi turunu calistirsaydi, KENDI
/// konusma kimligini uretir ve sonraki kaydetme kazananin durumunu sessizce
/// ezerdi — asil kusur buydu. Bu yuzden kaybeden acik bir catisma hatasi alir;
/// yeniden deneme normal (yaris disi) yolu izler ve bu kez kazananin ZATEN
/// yerlesmis kaydini bulur.
/// </para>
/// </remarks>
public sealed class AgentPrismSessionConflictException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string SessionConflictErrorType = "session_conflict";

    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismSessionConflictException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismSessionConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismSessionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Catisan oturumun kimligi.</summary>
    public string? SessionId { get; init; }

    /// <inheritdoc />
    public override string ErrorType => SessionConflictErrorType;
}

/// <summary>
/// Bir dis cagiran (MCP veya A2A uzerinden) katalogdaki bir agent'i cagirmak
/// istedi ama sinir ihlali nedeniyle reddedildi (Faz 50).
/// </summary>
/// <remarks>
/// En sik sebep: dis cagiran onaylı bir tool taşıyan bir agent'i acmaya
/// calisiyor. Dis cagiran bir agent degildir ve onay isteğine cevap veremez —
/// K-103'un aynisi, ikinci bir uygulaması.
/// </remarks>
public sealed class AgentPrismExternalCallException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string ExternalCallRejectedErrorType = "external_call_rejected";

    /// <summary>Yeni bir hata olusturur.</summary>
    public AgentPrismExternalCallException()
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public AgentPrismExternalCallException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir hata olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public AgentPrismExternalCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Cagrilmak istenen agent.</summary>
    public required string AgentName { get; init; }

    /// <summary>Cagrinin geldigi protokol: <c>mcp</c> veya <c>a2a</c>.</summary>
    public required string Protocol { get; init; }

    /// <inheritdoc />
    public override string ErrorType => ExternalCallRejectedErrorType;
}
