using System.Globalization;

namespace AgentPrism;

/// <summary>
/// Yurutme oturumu kimliklerini dogrular.
/// </summary>
/// <remarks>
/// 🚨 <strong>Oturum kimligi istemciden gelir ve guvenilmez girdidir.</strong>
/// Kontrol noktalari bu deger altinda gruplanir; dogrulanmadan kullanilmasi,
/// bir kullanicinin baska bir yurutmenin durumunu okumasina veya uzerine
/// yazmasina yol acar. Ayni dogrulama Faz 4'te <c>conversation_id</c> icin
/// yapilmisti.
/// </remarks>
internal static class WorkflowSessionId
{
    /// <summary>Kabul edilen en fazla karakter sayisi.</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Verilen kimligi dogrular; bos ise yeni bir kimlik uretir.
    /// </summary>
    /// <param name="sessionId">Istemciden gelen kimlik.</param>
    /// <returns>Kullanilabilir kimlik.</returns>
    /// <exception cref="AgentPrismException">Kimlik gecerli bicimde degilse.</exception>
    public static string Require(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return AgentPrismId.NewId().ToString("n", CultureInfo.InvariantCulture);
        }

        if (sessionId.Length > MaxLength)
        {
            throw new AgentPrismException(
                $"Yurutme oturumu kimligi en fazla {MaxLength} karakter olabilir.");
        }

        // Regex yerine elle dongu: MA0009 zaman asimi verilemeyen her regex'i
        // isaretler ve bu kadar basit bir desende regex zaten gereksizdir.
        foreach (var character in sessionId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                throw new AgentPrismException(
                    "Yurutme oturumu kimligi yalnizca harf, rakam, '-' ve '_' icerebilir.");
            }
        }

        return sessionId;
    }
}
