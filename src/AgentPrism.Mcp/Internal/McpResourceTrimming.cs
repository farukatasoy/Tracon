using System.Text;

namespace AgentPrism;

/// <summary>
/// MCP kaynak icerigini bir bayt sinirina gore kirpar.
/// </summary>
/// <remarks>
/// Saf ve durumsuzdur; agi veya <c>McpClient</c>'i gerektirmez, bu yuzden
/// dogrudan birim testiyle dogrulanir (docs/22-MCP-DERINLESMESI.md, Testler tablosu).
/// </remarks>
internal static class McpResourceTrimming
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Metni UTF-8 bayt sinirina gore kirpar. Cok baytli bir karakterin
    /// ortasindan kesmez; boyle bir durumda karakterin tamami atilir.
    /// </summary>
    /// <param name="text">Kirpilacak metin.</param>
    /// <param name="maxBytes">Ust bayt siniri.</param>
    /// <returns>Kirpilmis metin ve kirpma yapilip yapilmadigi.</returns>
    public static (string Text, bool Truncated) Trim(string text, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (maxBytes <= 0)
        {
            return (string.Empty, text.Length > 0);
        }

        var byteCount = Encoding.UTF8.GetByteCount(text);

        if (byteCount <= maxBytes)
        {
            return (text, false);
        }

        var bytes = Encoding.UTF8.GetBytes(text);

        // UTF-8'de bir karakter en fazla 4 bayttir; kesim noktasi geçersiz bir
        // dizinin ortasina denk gelirse en fazla 3 bayt geri cekilerek gecerli
        // bir sinir bulunur.
        for (var length = maxBytes; length > 0 && length > maxBytes - 4; length--)
        {
            try
            {
                return (StrictUtf8.GetString(bytes, 0, length), true);
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return (string.Empty, true);
    }
}
