using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// Insan girdisi bekleyen en kucuk workflow: soru sorar, evet/hayir bekler,
/// sonuca gore cikti uretir.
/// </summary>
/// <remarks>
/// Model cagrisi yoktur. Aranan sey arayuzun bekleyen istek kartini gosterip
/// gosteremedigi; araya bir model koymak testi yavaslatir ve hatanin nerede
/// oldugunu bulaniklastirirdi.
/// </remarks>
internal static class ApprovalWorkflow
{
    /// <summary>Dis istek portunun kimligi. Grafta bir dugum olarak gorunur.</summary>
    public const string PortId = "onay-portu";

    /// <summary>Grafi kurar.</summary>
    public static Workflow Build()
    {
        var port = RequestPort.Create<string, bool>(PortId);
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Onay ister: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "baslangic");

        // 🚨 Cikti tipi ISLEYICININ DONUS TIPINDEN bildirilir; govdesinde
        // YieldOutputAsync cagiran donussuz bir isleyici hicbir cikti tipi beyan
        // etmez ve calisma aninda duser (Faz 16'da olculdu).
        var end = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "onaylandi" : "reddedildi",
            id: "bitis");

        return new WorkflowBuilder(start)
            .AddEdge(start, portBinding)
            .AddEdge(portBinding, end)
            .WithOutputFrom(end)
            .WithName("onay-akisi")
            .Build();
    }
}
