using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir model saglayicisi. Her saglayici kendi paketinde uygulanir
/// (ornegin <c>AgentPrism.OpenAI</c>) ve DI'ya kaydedilir.
/// </summary>
/// <remarks>
/// Bu soyutlama, yeni bir saglayici eklemenin <em>kirici degisiklik olmamasini</em>
/// saglar. Modiler paketleme kararinin ana gerekcesi budur.
/// </remarks>
public interface IModelProvider
{
    /// <summary>
    /// Saglayici adi. <see cref="ModelBinding.Provider"/> bu degerle eslesir.
    /// Karsilastirma buyuk/kucuk harfe duyarli degildir.
    /// </summary>
    string Name { get; }

    /// <summary>Bu saglayicinin sundugu modeller.</summary>
    IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>
    /// Verilen baglanti icin <strong>ham</strong> bir sohbet istemcisi uretir.
    /// </summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <returns>
    /// Saglayiciya ozgu istemci. Saglayiciya <em>ozgu</em> dekoratorler (ornek:
    /// Anthropic'in ayar dekoratoru) burada eklenebilir.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🚨 <strong>Ortak boru hattini kurma.</strong> <c>UseFunctionInvocation()</c>,
    /// <c>UseOpenTelemetry()</c>, icerik guard'i, devre kesici ve ek cozme
    /// <c>ModelProviderRegistry.CreateChatClient</c> tarafindan eklenir. Faz 48'e
    /// kadar tool cagri dongusunu her saglayici paketi kendi icinde kuruyordu;
    /// bunun sonucu, defterin sardigi hicbir halkanin dongunun turlarini
    /// gorememesiydi — bir tool sonucu modele denetlenmeden giriyordu.
    /// </para>
    /// <para>
    /// Dongu burada da kurulursa ic ice iki <c>FunctionInvokingChatClient</c>
    /// olusur: ictekisi tool'lari cozer, distakisi hicbir cagri gormez. Zarari
    /// islevsel degil, olculebilirdir (iki kat sarmalama, yaniltici span agaci).
    /// </para>
    /// </remarks>
    IChatClient CreateChatClient(ModelBinding binding);
}
