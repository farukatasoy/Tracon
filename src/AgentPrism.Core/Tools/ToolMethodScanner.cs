using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
/// Statik metotlar dogrudan baglanir. Ornek metotlarinda tasiyici nesne her cagride
/// <see cref="AIFunctionArguments.Services"/> uzerinden cozulur; boylece tool sinifi
/// bagimlilik enjeksiyonundan servis alabilir ve durumu cagrilar arasinda sizmaz.
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

        if (method.IsStatic)
        {
            return AIFunctionFactory.Create(method, target: null, options);
        }

        // Ornek metodu: tasiyici nesne cagri aninda cozulur. Servis saglayici yoksa
        // hata acik olmalidir; sessizce yeni bir nesne uretmek, tool'un bagimliliklari
        // olmadan calismasina ve anlasilmaz sonuclar dondurmesine yol acardi.
        return AIFunctionFactory.Create(
            method,
            arguments => arguments.Services is { } services
                ? ActivatorUtilities.GetServiceOrCreateInstance(services, type)
                : throw new AgentPrismException(
                    $"'{type.FullName}.{method.Name}' bir ornek metodudur ve tasiyici nesnesi servis saglayicidan " +
                    "cozulur. Cagri baglaminda servis saglayici yok. Metodu `static` yapin veya tool'u " +
                    "`AddTool(AIFunctionFactory.Create(...))` ile hazir bir ornek uzerinden kaydedin."),
            options);
    }
}
