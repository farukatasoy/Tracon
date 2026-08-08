using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir tipteki <see cref="AgentPrismToolAttribute"/> ile isaretlenmis metotlari
/// bulur ve <see cref="AIFunction"/> nesnelerine donusturur.
/// </summary>
/// <remarks>
/// <para>
/// Tarama yansima kullanir. Bu yuzden cagiran her yol
/// <see cref="RequiresUnreferencedCodeAttribute"/> ve
/// <see cref="RequiresDynamicCodeAttribute"/> ile isaretlidir; uyari bastirilmaz,
/// cagirana iletilir. AOT hedefleyen uygulamalar
/// <see cref="IAgentPrismBuilder.AddTool(AIFunction, bool)"/> kullanmalidir.
/// </para>
/// <para>
/// 🚨 Yalnizca <strong>statik</strong> metotlar desteklenir (karar K-218).
/// MAF, tool govdesine <see cref="AIFunctionArguments.Services"/> olarak BOS bir
/// saglayici gecirir (<c>Microsoft.Extensions.AI.EmptyServiceProvider</c>); bir
/// ornek metodun tasiyici nesnesi bu yoldan COZULEMEZ. Bir ornek metodu
/// isaretlemek <see cref="Scan"/> anında (calisma anininin en erken noktasinda,
/// ilk tool cagrisini beklemeden) <see cref="AgentPrismException"/> firlatir.
/// Ornek metot tool'lari icin tool'u kurulum aninda ornekleyip
/// <c>AddTool(AIFunctionFactory.Create(...))</c> ile kaydedin.
/// </para>
/// </remarks>
internal static class ToolMethodScanner
{
    /// <summary>Isaretli metotlari bulur ve tool kayitlarina donusturur.</summary>
    /// <param name="type">Taranacak tip.</param>
    /// <returns>Bulunan tool kayitlari.</returns>
    /// <exception cref="AgentPrismException">
    /// Hicbir isaretli metot yoksa veya isaretli bir metot tool'a donusturulemiyorsa.
    /// </exception>
    [RequiresUnreferencedCode("Tool taramasi yansima kullanir; kirpilmis uygulamalarda metot bilgisi kaybolabilir.")]
    [RequiresDynamicCode("Tool taramasi calisma aninda kod uretimi gerektirebilir.")]
    public static List<AgentPrismToolRegistration> Scan(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var registrations = new List<AgentPrismToolRegistration>();

        foreach (var method in type.GetMethods(Flags))
        {
            var attribute = method.GetCustomAttribute<AgentPrismToolAttribute>(inherit: false);

            if (attribute is null)
            {
                continue;
            }

            registrations.Add(new AgentPrismToolRegistration(
                CreateFunction(type, method, attribute),
                attribute.RequiresApproval));
        }

        if (registrations.Count == 0)
        {
            throw new AgentPrismException(
                $"'{type.FullName}' tipinde [AgentPrismTool] ile isaretlenmis metot yok. " +
                "Tool olarak sunulacak metotlari isaretleyin veya tek tek `AddTool(...)` ile kaydedin.");
        }

        return registrations;
    }

    [RequiresUnreferencedCode("Tool taramasi yansima kullanir; kirpilmis uygulamalarda metot bilgisi kaybolabilir.")]
    [RequiresDynamicCode("Tool taramasi calisma aninda kod uretimi gerektirebilir.")]
    private static AIFunction CreateFunction(Type type, MethodInfo method, AgentPrismToolAttribute attribute)
    {
        if (method.IsGenericMethodDefinition)
        {
            throw new AgentPrismException(
                $"'{type.FullName}.{method.Name}' metodu [AgentPrismTool] ile isaretli ancak generic. " +
                "Tool metotlari generic olamaz; somut bir sarmalayici metot yazin.");
        }

        var options = new AIFunctionFactoryOptions
        {
            Name = attribute.Name ?? method.Name,
            Description = attribute.Description,
        };

        if (!method.IsStatic)
        {
            // K-218: MAF, AIFunctionArguments.Services olarak BOS bir saglayici gecirir
            // (EmptyServiceProvider, null DEGIL). Bu denetim eskiden cagri aninda,
            // servis cozumu icinde yasiyordu ve hicbir zaman calismiyordu - `is { }`
            // deseni hep dogru donuyordu. Onarim: denetim TARAMA anina alinir, burada
            // kesin calisir.
            throw new AgentPrismException(
                $"'{type.FullName}.{method.Name}' bir ornek metodudur ve tool olamaz. MAF, tool govdesine " +
                "AIFunctionArguments.Services olarak bos bir saglayici gecirir (karar K-218). Metodu `static` " +
                "yapin veya tool'u kurulum aninda ornekleyip `AddTool(AIFunctionFactory.Create(...))` ile kaydedin.");
        }

        return AIFunctionFactory.Create(method, target: null, options);
    }
}
