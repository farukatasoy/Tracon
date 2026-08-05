namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Konusma protokolunun durum makinesini dogrular.
/// </summary>
/// <remarks>
/// Durum makinesi saftir: ag, ses ve agent yoktur. Protokolun dogrulugu bu
/// yuzden bir WebSocket kurmadan denetlenebilir.
/// </remarks>
public sealed class VoiceConversationStateMachineTests
{
    [Fact]
    public void Baslangicta_yalniz_start_kabul_edilir()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Audio().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Start().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Listening);
    }

    [Fact]
    public void Ikinci_start_REDDEDILIR()
    {
        // Agent, oturum ve kiraci baglanti boyunca sabittir (29.3): ikinci bir
        // `start` yetkilendirmeyi el sikismadan sonraya tasirdi.
        var machine = new VoiceConversationStateMachine();

        machine.Start().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.Start().ShouldBe(VoiceTransitionOutcome.Rejected);
    }

    [Fact]
    public void Tam_bir_tur_start_commit_yanit_bitis()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Audio().ShouldBe(VoiceTransitionOutcome.Accepted);

        machine.Commit().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Transcribing);

        machine.BeginResponse().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Responding);

        machine.FinishTurn(counted: true).ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Listening);
        machine.Turns.ShouldBe(1);
    }

    [Fact]
    public void Agent_konusurken_gelen_ses_YOK_SAYILIR()
    {
        // Kesinti acik bir `cancel` ile istenir. Sesin kendisini kesinti sayan
        // bir sunucu, hoparlorden gelen kendi sesini kullanicinin sesi sanabilirdi.
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();
        machine.BeginResponse();

        machine.Audio().ShouldBe(VoiceTransitionOutcome.Ignored);
        machine.State.ShouldBe(VoiceConversationState.Responding);
    }

    [Fact]
    public void Mesgulken_gelen_commit_REDDEDILIR()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();

        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);
    }

    [Fact]
    public void Kesinti_durumu_DEGISTIRMEZ_donusu_yalniz_tur_gorevi_yapar()
    {
        // 🚨 Kesinti Listening'e cekseydi istemci hemen yeni bir `commit`
        // gonderebilir ve iki tur ayni anda calisirdi.
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();
        machine.BeginResponse();

        machine.Cancel().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Responding);
        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);

        machine.FinishTurn(counted: true);
        machine.State.ShouldBe(VoiceConversationState.Listening);
        machine.Turns.ShouldBe(1);
    }

    [Fact]
    public void Dinlerken_gelen_kesinti_dinlemeyi_SURDURUR()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();

        machine.Cancel().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.State.ShouldBe(VoiceConversationState.Listening);
    }

    [Fact]
    public void Bos_tur_sayilmaz()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();
        machine.FinishTurn(counted: false);

        machine.Turns.ShouldBe(0);
        machine.State.ShouldBe(VoiceConversationState.Listening);
    }

    [Fact]
    public void Stop_her_durumdan_kapatir_ve_sonrasi_kabul_edilmez()
    {
        var machine = new VoiceConversationStateMachine();

        machine.Start();
        machine.Commit();

        machine.Stop().ShouldBe(VoiceTransitionOutcome.Accepted);
        machine.IsClosed.ShouldBeTrue();

        machine.Stop().ShouldBe(VoiceTransitionOutcome.Ignored);
        machine.Start().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Audio().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.Commit().ShouldBe(VoiceTransitionOutcome.Rejected);
        machine.FinishTurn(counted: true).ShouldBe(VoiceTransitionOutcome.Ignored);
        machine.Turns.ShouldBe(0);
    }
}
