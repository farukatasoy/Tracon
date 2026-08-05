namespace AgentPrism;

/// <summary>Bir konusma baglantisinin durumu.</summary>
internal enum VoiceConversationState
{
    /// <summary><c>start</c> henuz gelmedi.</summary>
    New,

    /// <summary>Mikrofon acik; ses parcalari birikiyor.</summary>
    Listening,

    /// <summary>Biriken ses metne cevriliyor.</summary>
    Transcribing,

    /// <summary>Agent calisiyor ve/veya yanit seslendiriliyor.</summary>
    Responding,

    /// <summary>Baglanti kapandi.</summary>
    Closed,
}

/// <summary>Bir protokol olayinin sonucu.</summary>
internal enum VoiceTransitionOutcome
{
    /// <summary>Olay kabul edildi ve durum degisti.</summary>
    Accepted,

    /// <summary>
    /// Olay bu durumda anlamsizdi ve sessizce atildi.
    /// </summary>
    /// <remarks>
    /// Yok sayma <strong>hata degildir</strong>: bir yaris kosulunda gec kalan
    /// cerceve (ornegin tur bittikten sonra gelen <c>cancel</c>) normaldir ve
    /// istemciye hata gostermek gurultu uretirdi.
    /// </remarks>
    Ignored,

    /// <summary>Olay protokolu ihlal etti; istemciye hata bildirilir.</summary>
    Rejected,
}

/// <summary>
/// Konusma protokolunun saf durum makinesi.
/// </summary>
/// <remarks>
/// <para>
/// Burada ag, ses veya agent yoktur — yalnizca "hangi olay hangi durumda
/// gecerlidir" sorusu. Boylece protokol, bir WebSocket kurmadan birim testiyle
/// dogrulanabilir.
/// </para>
/// <para>
/// 🚨 Sinif <strong>es zamanlidir</strong> ve kendi kilidini tasimaz. Olaylar
/// iki kaynaktan gelir (alma dongusu ve tur gorevi), bu yuzden erisimi
/// serilestirmek <see cref="VoiceConversationDriver"/>'in sorumlulugudur.
/// </para>
/// </remarks>
internal sealed class VoiceConversationStateMachine
{
    /// <summary>Simdiki durum.</summary>
    public VoiceConversationState State { get; private set; } = VoiceConversationState.New;

    /// <summary>Tamamlanan tur sayisi.</summary>
    public int Turns { get; private set; }

    /// <summary>Baglanti kapandi mi.</summary>
    public bool IsClosed => State == VoiceConversationState.Closed;

    /// <summary>Ses parcasi kabul edilir mi.</summary>
    public bool IsListening => State == VoiceConversationState.Listening;

    /// <summary>Bir turu isleyen gorev calisiyor mu.</summary>
    public bool IsBusy => State is VoiceConversationState.Transcribing or VoiceConversationState.Responding;

    /// <summary><c>start</c> olayini isler.</summary>
    /// <returns>Sonuc.</returns>
    /// <remarks>
    /// Ikinci bir <c>start</c> agent'i degistirmek anlamina gelirdi; agent,
    /// oturum ve kiraci baglanti boyunca <strong>sabittir</strong> (29.3).
    /// </remarks>
    public VoiceTransitionOutcome Start() => State == VoiceConversationState.New
        ? Move(VoiceConversationState.Listening)
        : VoiceTransitionOutcome.Rejected;

    /// <summary>Bir ikili ses cercevesini isler.</summary>
    /// <returns>Sonuc.</returns>
    /// <remarks>
    /// Agent konusurken gelen ses <strong>yok sayilir</strong>. Kesinti acik bir
    /// <c>cancel</c> mesajiyla istenir; sesin kendisini kesinti sayan bir sunucu,
    /// hoparlorden gelen kendi sesini kullanicinin sesi sanabilirdi.
    /// </remarks>
    public VoiceTransitionOutcome Audio() => State switch
    {
        VoiceConversationState.Listening => VoiceTransitionOutcome.Accepted,
        VoiceConversationState.Transcribing or VoiceConversationState.Responding => VoiceTransitionOutcome.Ignored,
        _ => VoiceTransitionOutcome.Rejected,
    };

    /// <summary><c>commit</c> olayini isler.</summary>
    /// <returns>Sonuc.</returns>
    public VoiceTransitionOutcome Commit() => State == VoiceConversationState.Listening
        ? Move(VoiceConversationState.Transcribing)
        : VoiceTransitionOutcome.Rejected;

    /// <summary>Cozum bitti ve agent calismaya basliyor.</summary>
    /// <returns>Sonuc.</returns>
    public VoiceTransitionOutcome BeginResponse() => State == VoiceConversationState.Transcribing
        ? Move(VoiceConversationState.Responding)
        : VoiceTransitionOutcome.Rejected;

    /// <summary><c>cancel</c> olayini isler.</summary>
    /// <returns>Sonuc.</returns>
    /// <remarks>
    /// <para>
    /// Dinlerken gelen <c>cancel</c> biriken sesi atar ve dinlemeye devam eder;
    /// bu, kullanicinin "yanlis konustum" demesidir. Mesgulken gelen
    /// <c>cancel</c> turu keser.
    /// </para>
    /// <para>
    /// 🚨 Kesinti durumu <strong>degistirmez</strong>. Dinlemeye donusu yalnizca
    /// <see cref="FinishTurn"/> yapar; aksi halde istemci hemen yeni bir
    /// <c>commit</c> gonderebilir ve iki tur ayni anda calisirdi.
    /// </para>
    /// </remarks>
    public VoiceTransitionOutcome Cancel() => State switch
    {
        VoiceConversationState.Listening
            or VoiceConversationState.Transcribing
            or VoiceConversationState.Responding => VoiceTransitionOutcome.Accepted,
        _ => VoiceTransitionOutcome.Ignored,
    };

    /// <summary>Bir tur tamamlandi (kesilmis olsa bile).</summary>
    /// <param name="counted">Tur sayaci artsin mi.</param>
    /// <returns>Sonuc.</returns>
    /// <remarks>
    /// Kesilen bir tur de bir turdur ve sayilir: model bir yanit uretmeye
    /// baslamis, kullanici da onu duymustur.
    /// </remarks>
    public VoiceTransitionOutcome FinishTurn(bool counted)
    {
        if (State == VoiceConversationState.Closed)
        {
            return VoiceTransitionOutcome.Ignored;
        }

        if (counted)
        {
            Turns++;
        }

        return Move(VoiceConversationState.Listening);
    }

    /// <summary><c>stop</c> olayini veya soketin kapanmasini isler.</summary>
    /// <returns>Sonuc.</returns>
    public VoiceTransitionOutcome Stop()
        => State == VoiceConversationState.Closed
            ? VoiceTransitionOutcome.Ignored
            : Move(VoiceConversationState.Closed);

    private VoiceTransitionOutcome Move(VoiceConversationState next)
    {
        State = next;
        return VoiceTransitionOutcome.Accepted;
    }
}
