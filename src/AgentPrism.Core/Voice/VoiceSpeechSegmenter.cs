using System.Buffers;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Akan metni seslendirilebilir parcalara boler.
/// </summary>
/// <remarks>
/// <para>
/// Gecikmenin kaynagi burasidir. Yanitin tamami beklenip tek seferde
/// seslendirilseydi kullanici uzun bir sessizlik duyardi; her token ayri
/// seslendirilseydi ses bolunur ve saglayici cagrisi sayisi patlar.
/// Cumle siniri ikisinin arasindaki dogru noktadir.
/// </para>
/// <para>
/// Bolucu <strong>safdir</strong>: ag, ses ve zaman yoktur. Birim testi bu
/// yuzden ucuzdur ve gecikme davranisi kodun geri kalanindan bagimsiz
/// dogrulanir.
/// </para>
/// </remarks>
internal sealed class VoiceSpeechSegmenter
{
    /// <summary>
    /// Bir parcanin seslendirilmesi icin gereken en az karakter.
    /// </summary>
    /// <remarks>
    /// "Evet." gibi tek kelimelik bir parca kendi basina seslendirilirse ses
    /// kopuk cikar; boyle bir parca bir sonrakiyle birlestirilir.
    /// </remarks>
    private const int MinSegmentLength = 12;

    /// <summary>
    /// Cumle siniri gelmese bile bolunecek uzunluk.
    /// </summary>
    /// <remarks>
    /// Noktalama kullanmayan bir model (veya liste ureten bir yanit) aksi halde
    /// hic bolunmez ve ilk ses yanitin sonunu beklerdi.
    /// </remarks>
    private const int MaxSegmentLength = 240;

    private static readonly SearchValues<char> Terminators = SearchValues.Create(".!?;:\n…");

    private readonly StringBuilder _pending = new();

    /// <summary>Yeni bir metin parcasi ekler ve hazir olan parcalari dondurur.</summary>
    /// <param name="delta">Modelden gelen metin parcasi.</param>
    /// <returns>Seslendirmeye hazir parcalar; yoksa bos.</returns>
    public IReadOnlyList<string> Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return [];
        }

        _pending.Append(delta);

        List<string>? ready = null;

        while (TryCut(out var segment))
        {
            (ready ??= []).Add(segment);
        }

        return (IReadOnlyList<string>?)ready ?? [];
    }

    /// <summary>Kalan metni parca olarak dondurur ve tamponu bosaltir.</summary>
    /// <returns>Kalan parca; yoksa <see langword="null"/>.</returns>
    public string? Flush()
    {
        var text = _pending.ToString().Trim();
        _pending.Clear();

        return text.Length > 0 ? text : null;
    }

    private bool TryCut(out string segment)
    {
        segment = string.Empty;

        var text = _pending.ToString();
        var cut = FindCut(text);

        if (cut <= 0)
        {
            return false;
        }

        segment = text[..cut].Trim();
        _pending.Remove(0, cut);

        if (segment.Length != 0)
        {
            return true;
        }

        // Yalnizca bosluk kesildi; kesme gerceklesti ama seslendirilecek bir sey
        // yok. Dongunun ilerlemesi icin true donmek yanlis olurdu.
        return TryCut(out segment);
    }

    /// <summary>Kesme noktasini bulur.</summary>
    /// <param name="text">Bekleyen metin.</param>
    /// <returns>Kesilecek karakter sayisi; kesilmeyecekse <c>0</c>.</returns>
    private static int FindCut(string text)
    {
        var span = text.AsSpan();

        for (var i = MinSegmentLength - 1; i < span.Length; i++)
        {
            if (!Terminators.Contains(span[i]))
            {
                continue;
            }

            // Sonu belirsiz bir noktalama (ornegin "3." veya kisaltma) bir sonraki
            // karakter gelene kadar cumle sonu SAYILMAZ; bekleyerek karar veririz.
            if (i + 1 >= span.Length)
            {
                break;
            }

            if (char.IsWhiteSpace(span[i + 1]) || span[i] == '\n')
            {
                return i + 1;
            }
        }

        if (span.Length < MaxSegmentLength)
        {
            return 0;
        }

        // Noktalama gelmedi: son bosluktan kes, kelimeyi ortadan bolme.
        var window = span[..MaxSegmentLength];
        var lastSpace = window.LastIndexOf(' ');

        return lastSpace > MinSegmentLength ? lastSpace + 1 : MaxSegmentLength;
    }
}
