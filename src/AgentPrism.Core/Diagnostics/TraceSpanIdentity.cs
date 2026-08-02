using System.Security.Cryptography;
using System.Text;

namespace AgentPrism;

/// <summary>
/// W3C trace/span kimliklerinden kararli veritabani kimlikleri turetir.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Neden turetiyoruz?</strong> Bir span tamamlandiginda ebeveyni henuz
/// tamamlanmamis olabilir; sirasiz gelen span'leri bir haritayla eslestirmek
/// hem durum tasimayi hem de ebeveyni bekleme mantigini gerektirirdi. Turetilmis
/// kimlikte ebeveynin kimligi <em>hesaplanabilir</em>: haritaya gerek yoktur.
/// </para>
/// <para>
/// Ek fayda: ayni span iki kez yazilirsa ayni kimlik uretilir, boylece yazma
/// islemi fikirsel olarak idempotent kalir.
/// </para>
/// <para>
/// SHA-256 kriptografik amacla degil, <em>carpismasiz dagitim</em> icin
/// kullanilir. Girdi zaten rastgele 24 bayttir; kisaltma pratikte carpismaz.
/// </para>
/// </remarks>
internal static class TraceSpanIdentity
{
    /// <summary>
    /// Bir span'in veritabani kimligini uretir.
    /// </summary>
    /// <param name="traceId">W3C trace kimligi (32 karakterlik onaltilik).</param>
    /// <param name="spanId">W3C span kimligi (16 karakterlik onaltilik).</param>
    /// <returns>Kararli kimlik.</returns>
    public static Guid ForSpan(string traceId, string spanId)
    {
        var length = Encoding.UTF8.GetByteCount(traceId) + 1 + Encoding.UTF8.GetByteCount(spanId);
        var buffer = length <= 128 ? stackalloc byte[length] : new byte[length];

        var written = Encoding.UTF8.GetBytes(traceId, buffer);
        buffer[written++] = (byte)':';
        Encoding.UTF8.GetBytes(spanId, buffer[written..]);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buffer, hash);

        return new Guid(hash[..16]);
    }
}
