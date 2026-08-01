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

    /// <summary>Verilen baglanti icin bir sohbet istemcisi uretir.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <returns>Sohbet istemcisi.</returns>
    IChatClient CreateChatClient(ModelBinding binding);
}
