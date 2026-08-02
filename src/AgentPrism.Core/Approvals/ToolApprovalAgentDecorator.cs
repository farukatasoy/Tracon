using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Katalogdan cozulen her agent'i Microsoft Agent Framework'un
/// <see cref="ToolApprovalAgent"/> sarmalayicisiyla sarar ve kalici
/// "bir daha sorma" kurallarini otomatik onay kurali olarak baglar.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Order"/> degeri 20'dir: uc dekoratorun en <em>icteki</em>si.
/// Onay, model cagrisina en yakin katmanda verilmelidir; disina alinsaydi
/// telemetri ve calistirma kaydi onay beklemesini kendi suresi icine katardi.
/// </para>
/// <para>
/// Hangi tool'un onay istedigine bu sinif karar <strong>vermez</strong>. Karar
/// <see cref="ToolRegistry"/> icinde verilir: onay isteyen tool
/// <c>ApprovalRequiredAIFunction</c> ile sarilir ve MAF cagriyi calistirmak
/// yerine <c>ToolApprovalRequestContent</c> uretir. Bu sinif yalnizca
/// <em>otomatik onay</em> kurallarini uygular.
/// </para>
/// </remarks>
public sealed class ToolApprovalAgentDecorator : IAgentDecorator
{
    private readonly ToolApprovalRuleEvaluator _evaluator;

    /// <summary>Yeni bir onay dekoratoru olusturur.</summary>
    /// <param name="evaluator">Kalici kurallari uygulayan degerlendirici.</param>
    /// <exception cref="ArgumentNullException"><paramref name="evaluator"/> <see langword="null"/> ise.</exception>
    public ToolApprovalAgentDecorator(ToolApprovalRuleEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(descriptor);

        var agentName = descriptor.Name;

        var options = new ToolApprovalAgentOptions
        {
            AutoApprovalRules =
            [
                context => _evaluator.IsAutoApprovedAsync(agentName, context.FunctionCallContent),
            ],
        };

        return new ToolApprovalAgent(agent, options);
    }
}
