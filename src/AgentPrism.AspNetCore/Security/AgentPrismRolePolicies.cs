using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// <c>MapAgentPrism()</c> caginda hangi rol policy'lerinin (<see cref="AgentPrismPolicies"/>)
/// tuketicinin authorization yapilandirmasinda kayitli oldugunu bir kez cozer.
/// </summary>
/// <remarks>
/// <para>
/// Bir policy kayitli degilse ilgili alan <see langword="null"/> doner ve
/// <see cref="RoleEndpointConventionBuilderExtensions.RequireRole"/> hicbir
/// yetkilendirme eklemez — uc yalnizca mevcut uc katmanli korumadan gecer
/// (eski davranis).
/// </para>
/// <para>
/// Cozumleme <see cref="IAuthorizationPolicyProvider.GetPolicyAsync(string)"/> ile
/// yapilir. Varsayilan saglayici bu sorguyu <c>AuthorizationOptions</c> icindeki
/// bir sozlukten senkron olarak yanitlar (<c>Task.FromResult</c>); <c>MapAgentPrism()</c>
/// da <c>app.Build()</c> sonrasi, istek isleme disinda cagrildigi icin
/// <c>GetAwaiter().GetResult()</c> burada guvenlidir.
/// </para>
/// </remarks>
internal sealed class AgentPrismRolePolicies
{
    private AgentPrismRolePolicies(string? reader, string? operatorPolicy, string? admin)
    {
        Reader = reader;
        Operator = operatorPolicy;
        Admin = admin;
    }

    /// <summary>Kayitliysa <see cref="AgentPrismPolicies.Reader"/>, degilse <see langword="null"/>.</summary>
    public string? Reader { get; }

    /// <summary>Kayitliysa <see cref="AgentPrismPolicies.Operator"/>, degilse <see langword="null"/>.</summary>
    public string? Operator { get; }

    /// <summary>Kayitliysa <see cref="AgentPrismPolicies.Admin"/>, degilse <see langword="null"/>.</summary>
    public string? Admin { get; }

    /// <summary>
    /// Rol policy'lerinin kayit durumunu cozer. <see cref="AgentPrismEndpointOptions.RequireRolePolicies"/>
    /// aciksa ve bir policy eksikse acilista hata verir.
    /// </summary>
    /// <param name="services">Kurulu servis saglayici.</param>
    /// <param name="options">Uc ayarlari.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="AgentPrismEndpointOptions.RequireRolePolicies"/> acik ama bir veya
    /// daha fazla rol policy'si kayitli degilse.
    /// </exception>
    public static AgentPrismRolePolicies Resolve(IServiceProvider services, AgentPrismEndpointOptions options)
    {
        var provider = services.GetService<IAuthorizationPolicyProvider>();

        var reader = IsRegistered(provider, AgentPrismPolicies.Reader) ? AgentPrismPolicies.Reader : null;
        var operatorPolicy = IsRegistered(provider, AgentPrismPolicies.Operator) ? AgentPrismPolicies.Operator : null;
        var admin = IsRegistered(provider, AgentPrismPolicies.Admin) ? AgentPrismPolicies.Admin : null;

        if (options.RequireRolePolicies)
        {
            var missing = new List<string>(3);

            if (reader is null)
            {
                missing.Add(AgentPrismPolicies.Reader);
            }

            if (operatorPolicy is null)
            {
                missing.Add(AgentPrismPolicies.Operator);
            }

            if (admin is null)
            {
                missing.Add(AgentPrismPolicies.Admin);
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"AgentPrismEndpointOptions.RequireRolePolicies acik ama su policy'ler " +
                    $"kayitli degil: {string.Join(", ", missing)}. builder.Services.AddAuthorization(...) " +
                    "icinde AgentPrismPolicies.Reader/Operator/Admin adlarini tanimlayin veya " +
                    "RequireRolePolicies'i kapatin.");
            }
        }

        return new AgentPrismRolePolicies(reader, operatorPolicy, admin);
    }

    private static bool IsRegistered(IAuthorizationPolicyProvider? provider, string policyName)
    {
        if (provider is null)
        {
            return false;
        }

        try
        {
            return provider.GetPolicyAsync(policyName).GetAwaiter().GetResult() is not null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }
}
