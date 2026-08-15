using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism.Workflows.UnitTests.Fakes;

/// <summary>
/// Insan girdisi bekleyen bir graf, ama giris dugumu <see cref="ApprovalWorkflow"/>'un
/// aksine duz bir <c>BindAsExecutor</c> DEGIL, gercek dunyadaki
/// <c>ozetle-ve-onayla</c> ile ayni sekilde bir <see cref="AIAgentBinding"/>'dir.
/// </summary>
/// <remarks>
/// MT-WF-062 (HATA): bir <c>respond</c> cagrisi giris dugumunu SIFIRDAN yeniden
/// tetikliyordu. <see cref="ApprovalWorkflow"/>'un duz executor'u bunu hic
/// yakalamaz — yalniz <c>TurnToken</c> ile tetiklenen bir agent-host dugumu
/// (bu sinif) kok nedeni gorunur kilar.
/// </remarks>
internal static class AgentApprovalWorkflow
{
    /// <summary>Dis istek portunun kimligi.</summary>
    public const string PortId = "yayin-onayi";

    /// <summary>Grafi kurar. <paramref name="summarizer"/> giris dugumudur.</summary>
    public static Workflow Build(AIAgent summarizer)
    {
        var port = RequestPort.Create<string, bool>(PortId);
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var ask = ExecutorBindingExtensions.BindAsExecutor(
            static (List<ChatMessage> messages) =>
                "Bu ozet yayinlansin mi?" + Environment.NewLine + Environment.NewLine +
                (messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                 ?? string.Empty),
            id: "onay-sorusu");

        var publish = ExecutorBindingExtensions.BindAsExecutor(
            static (bool approved) => approved
                ? "onaylandi"
                : "reddedildi",
            id: "yayin");

        var summarizeBinding = new AIAgentBinding(
            summarizer,
            new AIAgentHostOptions
            {
                EmitAgentResponseEvents = true,
                EmitAgentUpdateEvents = true,
                ForwardIncomingMessages = false,
            });

        return new WorkflowBuilder(summarizeBinding)
            .AddEdge(summarizeBinding, ask)
            .AddEdge(ask, portBinding)
            .AddEdge(portBinding, publish)
            .WithOutputFrom(publish)
            .WithName("ozetleyici-onay-akisi")
            .Build();
    }

    /// <summary>Kosucuya verilecek kod kaydi.</summary>
    public static CodeWorkflowRegistration Registration(AIAgent summarizer, string name = "ozetleyici-onay-akisi")
        => new(name, "Ozetler, sonra insan onayi bekler.", _ => Build(summarizer));
}
