using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>Bir ucun API anahtariyla cagrildiginda tasimasi gereken kapsam.</summary>
/// <param name="Scope">Gereken kapsam.</param>
/// <remarks>
/// Rol politikalarinin yerine GECMEZ; API anahtariyla dogrulanmis istekleri
/// EK olarak daraltir (bolum 53.3). Statik bearer token veya kullanici
/// kimligiyle gelen istekler bu denetimden etkilenmez — yalnizca
/// <see cref="ApiKeyRequestContext"/>'te bir kayit varsa uygulanir.
/// </remarks>
internal sealed record ApiKeyScopeRequirement(ApiKeyScope Scope);

/// <summary>Uc sozlesmesine bir kapsam gereksinimi ekleyen uzanti.</summary>
internal static class ApiKeyScopeEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Ucun bir API anahtariyla cagrilabilmesi icin <paramref name="scope"/>'u
    /// tasimasi gerektigini isaretler.
    /// </summary>
    /// <typeparam name="TBuilder">Sozlesme olusturucu tipi.</typeparam>
    /// <param name="builder">Uc sozlesme olusturucusu.</param>
    /// <param name="scope">Gereken kapsam.</param>
    /// <returns>Ayni olusturucu (zincirlenebilir).</returns>
    public static TBuilder RequireApiKeyScope<TBuilder>(this TBuilder builder, ApiKeyScope scope)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new ApiKeyScopeRequirement(scope));

        return builder;
    }
}
