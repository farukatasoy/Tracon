using System.Buffers.Binary;

namespace AgentPrism;

/// <summary>Biriken bir konusma parcasinin cozume hazir hali.</summary>
/// <param name="Data">Ses baytlari. PCM girdi icin WAV basligi eklenmistir.</param>
/// <param name="MediaType">MIME turu.</param>
/// <param name="Duration">
/// Sesin suresi; yalniz ham PCM'de <strong>hesaplanabilir</strong> (bayt/hiz).
/// Sikistirilmis bir kapta sure ancak cozulerek bulunur, bu yuzden
/// <see langword="null"/>'dur ve saglayicinin bildirdigi sure kullanilir.
/// </param>
internal readonly record struct VoiceUtterance(byte[] Data, string MediaType, TimeSpan? Duration);

/// <summary>
/// Istemciden gelen ikili ses cercevelerini bir konusma parcasinda biriktirir.
/// </summary>
/// <remarks>
/// <para>
/// Iki sinir uygulanir ve ikisi de gereklidir: <em>sure</em> siniri istemcinin
/// VAD'i hic tetiklenmediginde devreye girer, <em>bayt</em> siniri istemci ses
/// yerine rastgele veri gonderdiginde. Yalniz biri, digerinin durumunu
/// yakalamaz.
/// </para>
/// <para>
/// 🚨 Sinif es zamanlidir ve kilit tasimaz; tek bir alma dongusunden beslenir.
/// </para>
/// </remarks>
internal sealed class VoiceUtteranceBuffer
{
    /// <summary>PCM ornek basina bayt (16-bit mono).</summary>
    private const int PcmBytesPerSample = 2;

    private readonly List<byte[]> _chunks = [];
    private readonly string _format;
    private readonly int _sampleRate;
    private readonly int _maxBytes;
    private readonly long _maxPcmBytes;

    /// <summary>Yeni bir tampon kurar.</summary>
    /// <param name="format">Bkz. <see cref="VoiceAudioFormats"/>.</param>
    /// <param name="sampleRate">Ham PCM'in ornekleme hizi (Hz).</param>
    /// <param name="maxBytes">Parca basina en fazla bayt.</param>
    /// <param name="maxDuration">Parca basina en uzun sure.</param>
    public VoiceUtteranceBuffer(string format, int sampleRate, int maxBytes, TimeSpan maxDuration)
    {
        _format = string.IsNullOrWhiteSpace(format) ? VoiceAudioFormats.WebmOpus : format;
        _sampleRate = sampleRate > 0 ? sampleRate : 16_000;
        _maxBytes = Math.Max(1024, maxBytes);
        _maxPcmBytes = (long)(maxDuration.TotalSeconds * _sampleRate * PcmBytesPerSample);
    }

    /// <summary>Biriken bayt sayisi.</summary>
    public int Length { get; private set; }

    /// <summary>Tamponda ses var mi.</summary>
    public bool HasAudio => Length > 0;

    /// <summary>
    /// Sinirlardan biri asildi mi.
    /// </summary>
    /// <remarks>
    /// Sure siniri yalniz ham PCM'de bayt sayisindan hesaplanabilir. Sikistirilmis
    /// kaplarda sureyi <em>bilmedigimizi</em> kabul ediyoruz; orada koruma
    /// yalnizca bayt sinirindan gelir ve bu durum belgelenmistir.
    /// </remarks>
    public bool IsFull => Length >= _maxBytes || (IsPcm && _maxPcmBytes > 0 && Length >= _maxPcmBytes);

    private bool IsPcm => string.Equals(_format, VoiceAudioFormats.Pcm16, StringComparison.OrdinalIgnoreCase);

    /// <summary>Bir ses parcasi ekler.</summary>
    /// <param name="chunk">Ham baytlar.</param>
    /// <returns>
    /// Parca eklendiyse <see langword="true"/>; sinir dolduysa
    /// <see langword="false"/> (parca <strong>atilir</strong>).
    /// </returns>
    public bool Append(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty || IsFull)
        {
            return false;
        }

        var remaining = _maxBytes - Length;
        var take = Math.Min(remaining, chunk.Length);

        _chunks.Add(chunk[..take].ToArray());
        Length += take;

        return true;
    }

    /// <summary>Tamponu bosaltir.</summary>
    public void Clear()
    {
        _chunks.Clear();
        Length = 0;
    }

    /// <summary>
    /// Biriken sesi cozume hazir bir dosyaya cevirir ve tamponu bosaltir.
    /// </summary>
    /// <returns>Parca; ses yoksa <see langword="null"/>.</returns>
    public VoiceUtterance? Take()
    {
        if (Length == 0)
        {
            return null;
        }

        var payload = new byte[Length];
        var offset = 0;

        foreach (var chunk in _chunks)
        {
            chunk.CopyTo(payload, offset);
            offset += chunk.Length;
        }

        Clear();

        if (!IsPcm)
        {
            // WebM parcalari birlestirildiginde gecerli bir kap olusur; kabin
            // basligi ilk parcadadir.
            return new VoiceUtterance(payload, "audio/webm", null);
        }

        var seconds = (double)payload.Length / (_sampleRate * PcmBytesPerSample);

        return new VoiceUtterance(
            WriteWaveFile(payload, _sampleRate),
            "audio/wav",
            TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Ham 16-bit mono PCM'in onune 44 baytlik bir WAV (RIFF) basligi yazar.
    /// </summary>
    /// <param name="pcm">Ham ornekler.</param>
    /// <param name="sampleRate">Ornekleme hizi (Hz).</param>
    /// <returns>Gecerli bir WAV dosyasi.</returns>
    /// <remarks>
    /// <para>
    /// 🚨 Ham PCM tek basina bir dosya <strong>degildir</strong>: cozum ucu onu
    /// <c>multipart/form-data</c> icinde bir dosya olarak alir ve turunu
    /// baslikdan tanir. Baslik yazilmazsa saglayici sesi ya reddeder ya da
    /// yanlis hizda cozer.
    /// </para>
    /// <para>
    /// Baslik elle yazilir; bir ses kutuphanesi <strong>alinmaz</strong>. 44
    /// bayt sabittir ve bicimi otuz yildir degismemistir.
    /// </para>
    /// </remarks>
    internal static byte[] WriteWaveFile(ReadOnlySpan<byte> pcm, int sampleRate)
    {
        const int headerLength = 44;
        const short channels = 1;
        const short bitsPerSample = 16;

        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var file = new byte[headerLength + pcm.Length];
        var span = file.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + pcm.Length));
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)pcm.Length);

        pcm.CopyTo(span[headerLength..]);

        return file;
    }
}
