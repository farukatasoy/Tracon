using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Kodda kayitli tool'larin defteri.
/// </summary>
/// <remarks>
/// Bu defter AgentPrism'in <strong>guvenlik sinirlarindan biridir</strong>.
/// Bir agent tanimi yalnizca burada kayitli bir tool'a ad ile isaret edebilir.
/// Arayuzden tool <em>kodu</em> yazilamaz; yalnizca kayitli tool'lardan secim yapilir.
/// Boylece arayuze erisen birinin sunucuda kod calistirmasi mumkun olmaz.
/// </remarks>
public interface IToolRegistry
{
    /// <summary>Kayitli tum tool'larin tanimlarini ada gore sirali dondurur.</summary>
    /// <returns>Tool tanimlari.</returns>
    IReadOnlyList<ToolDescriptor> List();

    /// <summary>Adi verilen tool'u getirir.</summary>
    /// <param name="name">Tool adi. Karsilastirma buyuk/kucuk harfe duyarlidir.</param>
    /// <param name="tool">Bulunan tool.</param>
    /// <returns>Tool kayitliysa <see langword="true"/>.</returns>
    bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool);
}
