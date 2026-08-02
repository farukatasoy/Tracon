using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;

namespace AgentPrism;

/// <summary>
/// Bir uca rol tabanli yetkilendirme eklemenin kosullu yolunu saglar.
/// </summary>
internal static class RoleEndpointConventionBuilderExtensions
{
    /// <summary>
    /// <paramref name="policyName"/> <see langword="null"/> degilse uca
    /// <c>RequireAuthorization(policyName)</c> ekler; <see langword="null"/> ise
    /// hicbir sey yapmaz.
    /// </summary>
    /// <remarks>
    /// <paramref name="policyName"/>'in <see langword="null"/> olmasi, ilgili
    /// <see cref="AgentPrismPolicies"/> policy'sinin tuketicinin authorization
    /// yapilandirmasinda kayitli olmadigi anlamina gelir — bu durumda uc yalnizca
    /// mevcut uc katmanli korumadan (loopback, bearer, genel policy) gecer.
    /// </remarks>
    /// <param name="builder">Uc olusturucu.</param>
    /// <param name="policyName">Uygulanacak policy adi; kayitli degilse <see langword="null"/>.</param>
    /// <returns>Aynen devam eden olusturucu.</returns>
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, string? policyName)
        where TBuilder : IEndpointConventionBuilder
    {
        if (policyName is not null)
        {
            builder.RequireAuthorization(policyName);
        }

        return builder;
    }
}
