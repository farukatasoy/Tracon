using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism.Workflows.UnitTests.Fakes;

/// <summary>
/// Insan girdisi bekleyen en kucuk workflow: bir soru sorar, evet/hayir bekler,
/// sonuca gore cikti uretir.
/// </summary>
/// <remarks>
/// <para>
/// Model cagrisi <strong>yoktur</strong>. Human-in-the-loop akisinin sinandigi
/// yer yurutme motorunun kendisidir; araya bir model koymak testi yavaslatir ve
/// hatanin nerede oldugunu bulaniklastirirdi.
/// </para>
/// <para>
/// 🚨 <c>WithOutputFrom</c> zorunludur. Cagirilmazsa graf calisir ama cikti
/// uretmeye calisan executor <c>Cannot output object of type String. Expecting
/// one of []</c> ile duser - olculdu (Faz 16).
/// </para>
/// </remarks>
internal static class ApprovalWorkflow
{
    /// <summary>Dis istek portunun kimligi. Graf dugumu olarak da bu adla gorunur.</summary>
    public const string PortId = "onay-portu";

    /// <summary>Grafi kurar.</summary>
    public static Workflow Build()
    {
        var port = RequestPort.Create<string, bool>(PortId);
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Onay ister: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "baslangic");

        // 🚨 Cikti isleyicinin DONUS TIPINDEN bildirilir. Govdesinde
        // YieldOutputAsync cagiran, donusu olmayan bir isleyici hicbir cikti tipi
        // beyan etmez ve calisma aninda "Cannot output object of type String.
        // Expecting one of []" ile duser - olculdu (Faz 16).
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

    /// <summary>Kosucuya verilecek kod kaydi.</summary>
    public static CodeWorkflowRegistration Registration(string name = "onay-akisi")
        => new(name, "Insan onayi bekleyen akis.", _ => Build());
}
