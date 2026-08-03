using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Bir workflow'a baglanan agent sarmalayicisina <strong>kalici</strong> bir
/// kimlik verir; boylece kontrol noktalari surec yeniden baslasa da gecerli kalir.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Neden gerekli.</strong> Microsoft Agent Framework executor
/// kimliklerini agent <em>orneginden</em> turetir: kimlik
/// <c>{Name}_{AIAgent.Id}</c> bicimindedir ve <c>AIAgent.Id</c> her ornek icin
/// rastgele uretilir. Faz 15 bunu bir surec ici onbellekle
/// (<see cref="WorkflowAgentCache"/>) sabitledi, ama onbellek surec belleginde
/// yasar: uygulama yeniden baslatildiginda kimlikler degisir ve MAF eski
/// kontrol noktasini <c>InvalidDataException</c> ile reddeder. Bir insan
/// yanitini bekleyen calistirma bu yuzden her dagitimda kaybolurdu.
/// </para>
/// <para>
/// <strong>Grafta kimligi degisken olan tek sey agent executor'udur.</strong>
/// Faz 16'da olculdu: hazir desenlerin urettigi diger butun executor kimlikleri
/// (<c>OutputMessages</c>, <c>Start</c>, <c>Batcher/*</c>, <c>ConcurrentEnd</c>,
/// <c>HandoffStart</c>, <c>HandoffEnd</c>, <c>GroupChatHost</c>,
/// <c>MagenticOrchestrator</c>) zaten sabittir. Dolayisiyla yalnizca
/// <c>AIAgent.Id</c> sabitlenirse <strong>bes desenin tamami</strong> kalici
/// kimlik kazanir ve grafi elle kurmak gerekmez.
/// </para>
/// <para>
/// 🚨 <strong>Kimlik ozel bir alana yazilir.</strong> <c>AIAgent.Id</c> sanal
/// degildir ve yazilabilir degildir; turetilmis bir sinif onu degistiremez
/// (reflection ile dogrulandi). Tek yol, taban sinifin otomatik ozellik alanini
/// (<c>&lt;Id&gt;k__BackingField</c>) yazmaktir. Yazma <strong>yalnizca
/// AgentPrism'in kendi sarmalayici ornegi uzerinde</strong> yapilir; MAF'in
/// kendi nesnelerine dokunulmaz. MAF bu alani kaldirirsa kimlik rastgele kalir
/// ve davranis Faz 15'e doner: hata mesaji zaten ne yapilmasi gerektigini
/// soyler. Sessiz bozulmayi <c>WorkflowAgentIdentityTests</c> engeller - kimlik
/// beklenen degeri tasimazsa test kirilir.
/// </para>
/// </remarks>
internal static class WorkflowAgentIdentity
{
    /// <summary>Derleyicinin <c>AIAgent.Id</c> otomatik ozelligi icin urettigi alan adi.</summary>
    private const string BackingFieldName = "<Id>k__BackingField";

    private static readonly FieldInfo? IdField = typeof(AIAgent)
        .GetField(BackingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Kalici kimlik verilebiliyor mu.</summary>
    /// <remarks>
    /// <see langword="false"/> ise Microsoft Agent Framework <c>AIAgent.Id</c>
    /// uygulamasini degistirmis demektir. Calistirma yine calisir; yalnizca
    /// surec yeniden baslatildiginda eski kontrol noktalari kullanilamaz.
    /// </remarks>
    public static bool IsSupported => IdField is not null;

    /// <summary>
    /// Bir <c>(workflow, agent)</c> cifti icin kalici kimlik uretir.
    /// </summary>
    /// <param name="workflowName">Sarmalayan workflow'un adi.</param>
    /// <param name="agentName">Baglanan agent'in adi.</param>
    /// <returns>32 karakterlik onaltilik kimlik.</returns>
    /// <remarks>
    /// Bicim <c>Guid.ToString("n")</c> ile aynidir. Bilerek: MAF'in urettigi
    /// kimlik de bu bicimdedir ve executor kimligi <c>{ad}_{kimlik}</c> olarak
    /// birlestirilir. Ad veya kimlik icinde ayirici karakter (<c>:</c>,
    /// <c>/</c>) tasimak, Mermaid dugum adlarini ve kontrol noktasi
    /// anahtarlarini gereksizce riske atardi.
    /// </remarks>
    public static string Compute(string workflowName, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        // Ayirici olarak '\n' kullanilir: workflow ve agent adlarinda gecemez,
        // dolayisiyla ("a-b", "c") ile ("a", "b-c") ayni kimligi uretemez.
        var seed = $"agentprism/workflow\n{workflowName}\n{agentName}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        // 16 bayt bir GUID kadar genistir; carpisma olasiligi anlamsiz kucukluktedir.
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sarmalayiciya kalici kimligi yazar. Yazilamazsa bir kez uyarir ve
    /// rastgele kimlikle devam eder.
    /// </summary>
    /// <param name="agent">Kimligi sabitlenecek sarmalayici.</param>
    /// <param name="workflowName">Sarmalayan workflow'un adi.</param>
    /// <param name="agentName">Baglanan agent'in adi.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <returns>Kimlik yazildiysa <see langword="true"/>.</returns>
    /// <remarks>
    /// Basarisizlik <strong>istisna atmaz</strong>. Kalici kimlik bir iyilestirmedir;
    /// olmadiginda workflow'lar Faz 15'teki gibi calisir ve yalnizca yeniden
    /// baslatma sonrasi sürdürme kaybolur. Butun workflow yurutmesini bir
    /// MAF ic degisikligi yuzunden durdurmak, orantisiz bir cezadir.
    /// </remarks>
    public static bool TryApply(AIAgent agent, string workflowName, string agentName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);

        if (IdField is null)
        {
            logger.LogWarning(
                "Workflow agent'i '{Agent}' icin kalici executor kimligi yazilamadi: " +
                "Microsoft Agent Framework '{Field}' alanini artik tasimiyor. " +
                "Calistirma normal calisir; ancak uygulama yeniden baslatildiginda " +
                "eski kontrol noktalari kullanilamaz.",
                agentName,
                BackingFieldName);

            return false;
        }

        IdField.SetValue(agent, Compute(workflowName, agentName));

        return true;
    }
}
