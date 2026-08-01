using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Kayitli model saglayicilarini ada gore tutan defter.</summary>
public interface IModelProviderRegistry
{
    /// <summary>Kayitli saglayicilarin tanimlarini dondurur.</summary>
    /// <returns>Saglayici tanimlari.</returns>
    IReadOnlyList<ModelProviderDescriptor> List();

    /// <summary>Verilen baglanti icin bir sohbet istemcisi uretir.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <returns>Sohbet istemcisi.</returns>
    /// <exception cref="AgentPrismException">
    /// <see cref="ModelBinding.Provider"/> adinda kayitli bir saglayici yoksa.
    /// </exception>
    IChatClient CreateChatClient(ModelBinding binding);
}
