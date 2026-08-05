using System.Buffers.Binary;

namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Konusma parcasi tamponunu ve WAV sarmalamasini dogrular.
/// </summary>
public sealed class VoiceUtteranceBufferTests
{
    [Fact]
    public void Ham_PCM_gecerli_bir_WAV_dosyasina_sarilir()
    {
        // 🚨 Ham PCM basliksizdir ve tek basina bir dosya DEGILDIR: cozum ucu onu
        // multipart icinde bir dosya olarak alir ve turunu baslikdan tanir.
        var buffer = Create(VoiceAudioFormats.Pcm16, maxBytes: 1_000_000);

        buffer.Append(new byte[3200]);

        var utterance = buffer.Take().ShouldNotBeNull();

        utterance.MediaType.ShouldBe("audio/wav");
        utterance.Data.Length.ShouldBe(3200 + 44);

        var header = utterance.Data.AsSpan();

        header[..4].SequenceEqual("RIFF"u8).ShouldBeTrue();
        header.Slice(8, 4).SequenceEqual("WAVE"u8).ShouldBeTrue();
        header.Slice(36, 4).SequenceEqual("data"u8).ShouldBeTrue();

        BinaryPrimitives.ReadUInt32LittleEndian(header[24..]).ShouldBe(16_000u);
        BinaryPrimitives.ReadInt16LittleEndian(header[22..]).ShouldBe((short)1);
        BinaryPrimitives.ReadInt16LittleEndian(header[34..]).ShouldBe((short)16);
        BinaryPrimitives.ReadUInt32LittleEndian(header[40..]).ShouldBe(3200u);
    }

    [Fact]
    public void Ham_PCM_suresi_bayt_sayisindan_HESAPLANIR()
    {
        // 16 kHz, mono, 16-bit -> saniyede 32.000 bayt.
        var buffer = Create(VoiceAudioFormats.Pcm16, maxBytes: 1_000_000);

        buffer.Append(new byte[32_000]);

        buffer.Take().ShouldNotBeNull().Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Sikistirilmis_kabin_suresi_BILINMEZ()
    {
        // Sure ancak cozerek bulunur; uydurmak yerine saglayicinin bildirdigi
        // deger kullanilir (K-032).
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 1_000_000);

        buffer.Append(new byte[1024]);

        var utterance = buffer.Take().ShouldNotBeNull();

        utterance.Duration.ShouldBeNull();
        utterance.MediaType.ShouldBe("audio/webm");
    }

    [Fact]
    public void Bayt_siniri_asilinca_tampon_dolar_ve_yeni_parca_ALINMAZ()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 2048);

        buffer.Append(new byte[4096]).ShouldBeTrue();
        buffer.Length.ShouldBe(2048);
        buffer.IsFull.ShouldBeTrue();

        buffer.Append(new byte[10]).ShouldBeFalse();
        buffer.Length.ShouldBe(2048);
    }

    [Fact]
    public void PCM_sure_siniri_bayt_sinirindan_ONCE_dolabilir()
    {
        // Guvenlik agi: istemcinin VAD'i hic tetiklenmezse parca sure
        // sinirinda kendiliginden kapanir.
        var buffer = new VoiceUtteranceBuffer(
            VoiceAudioFormats.Pcm16,
            sampleRate: 16_000,
            maxBytes: 10_000_000,
            maxDuration: TimeSpan.FromSeconds(1));

        buffer.Append(new byte[16_000]);
        buffer.IsFull.ShouldBeFalse();

        buffer.Append(new byte[16_000]);
        buffer.IsFull.ShouldBeTrue();
    }

    [Fact]
    public void Bos_tampon_parca_URETMEZ()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 4096);

        buffer.HasAudio.ShouldBeFalse();
        buffer.Take().ShouldBeNull();
    }

    [Fact]
    public void Take_tamponu_bosaltir()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 4096);

        buffer.Append(new byte[100]);
        buffer.Take().ShouldNotBeNull();

        buffer.Length.ShouldBe(0);
        buffer.Take().ShouldBeNull();
    }

    [Fact]
    public void Clear_biriken_sesi_atar()
    {
        var buffer = Create(VoiceAudioFormats.WebmOpus, maxBytes: 4096);

        buffer.Append(new byte[100]);
        buffer.Clear();

        buffer.HasAudio.ShouldBeFalse();
    }

    private static VoiceUtteranceBuffer Create(string format, int maxBytes)
        => new(format, sampleRate: 16_000, maxBytes: maxBytes, maxDuration: TimeSpan.FromSeconds(60));
}
