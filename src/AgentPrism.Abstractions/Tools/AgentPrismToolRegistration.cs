using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bagimlilik enjeksiyonuna kaydedilen tek bir tool. AgentPrism tool defterini
/// bu kayitlardan olusturur.
/// </summary>
/// <remarks>
/// Tuketici, tool'lari kendi DI modullerinden de kaydedebilir:
/// <code>
/// services.AddSingleton(new AgentPrismToolRegistration(myFunction));
/// </code>
/// </remarks>
public sealed class AgentPrismToolRegistration
{
    /// <summary>Yeni bir tool kaydi olusturur.</summary>
    /// <param name="function">Kaydedilecek tool.</param>
    /// <param name="requiresApproval">Cagri oncesi acik onay gerekip gerekmedigi.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> <see langword="null"/> ise.</exception>
    public AgentPrismToolRegistration(AIFunction function, bool requiresApproval = false)
    {
        ArgumentNullException.ThrowIfNull(function);

        Function = function;
        RequiresApproval = requiresApproval;
    }

    /// <summary>Kaydedilen tool.</summary>
    public AIFunction Function { get; }

    /// <summary>Cagri oncesi acik onay gerekip gerekmedigi.</summary>
    public bool RequiresApproval { get; }
}
