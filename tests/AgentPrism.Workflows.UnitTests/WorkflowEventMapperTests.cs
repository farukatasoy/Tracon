using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Olay eslemesi. Kritik nokta dal sirasidir: <c>AgentResponseEvent</c> ve
/// <c>AgentResponseUpdateEvent</c>, <c>WorkflowOutputEvent</c>'ten turer.
/// </summary>
public sealed class WorkflowEventMapperTests
{
    [Fact]
    public void Baslangic_olayi_eslenir()
    {
        var mapping = WorkflowEventMapper.Map(new WorkflowStartedEvent(message: null));

        mapping.IsKnown.ShouldBeTrue();
        mapping.Draft!.Value.Type.ShouldBe(RunEventType.WorkflowStarted);
    }

    [Fact]
    public void Agent_guncellemesi_cikti_DEGIL_metin_parcasi_olarak_eslenir()
    {
        // AgentResponseUpdateEvent, WorkflowOutputEvent'ten TUREDIGI icin genel
        // dal once yazilsaydi bu olay "workflow cikti uretti" diye siniflanir ve
        // gercek cikti kaybolurdu.
        var update = new AgentResponseUpdate(ChatRole.Assistant, "parca");
        var mapping = WorkflowEventMapper.Map(new AgentResponseUpdateEvent("yazar", update));

        mapping.IsKnown.ShouldBeTrue();
        mapping.Draft!.Value.Type.ShouldBe(RunEventType.MessageDelta);
        mapping.Draft!.Value.Text.ShouldBe("parca");
        mapping.Draft!.Value.ToolName.ShouldBe("yazar");
    }

    [Fact]
    public void Tam_agent_yaniti_atlanir()
    {
        // Akisli calistirmada guncellemeler zaten metni tasir; tam yaniti da
        // yazmak ayni metni akista iki kez gosterirdi.
        var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, "tam yanit"));
        var mapping = WorkflowEventMapper.Map(new AgentResponseEvent("yazar", response));

        mapping.IsKnown.ShouldBeTrue();
        mapping.Draft.ShouldBeNull();
    }

    [Fact]
    public void Workflow_ciktisi_metne_cevrilir()
    {
        List<ChatMessage> output =
        [
            new(ChatRole.User, "soru"),
            new(ChatRole.Assistant, "cevap"),
        ];

        var mapping = WorkflowEventMapper.Map(new WorkflowOutputEvent(output, "OutputMessages"));

        mapping.Draft!.Value.Type.ShouldBe(RunEventType.WorkflowOutput);
        (mapping.Draft!.Value.Text ?? string.Empty).ShouldContain("cevap", Case.Sensitive);
    }

    [Fact]
    public void Executor_hatasi_eslenir()
    {
        var mapping = WorkflowEventMapper.Map(
            new ExecutorFailedEvent("yazar", new InvalidOperationException("patladi")));

        mapping.Draft!.Value.Type.ShouldBe(RunEventType.ExecutorFailed);
        mapping.Draft!.Value.Text.ShouldBe("yazar");
        mapping.Draft!.Value.Payload.ShouldBe("patladi");
    }

    [Fact]
    public void Super_step_olaylari_adim_numarasini_tasir()
    {
        var started = WorkflowEventMapper.Map(
            new SuperStepStartedEvent(3, new SuperStepStartInfo(["yazar"])));

        started.Draft!.Value.Type.ShouldBe(RunEventType.SuperStepStarted);
        started.Draft!.Value.Text.ShouldBe("3");
        started.Draft!.Value.Payload.ShouldBe("yazar");
    }

    [Fact]
    public void Bilinmeyen_olay_taninmaz_olarak_isaretlenir()
    {
        // Sessizce dusurulen bir olay, MAF yeni bir tip ekledigi gun hata
        // ayiklamayi imkansizlastirirdi.
        var mapping = WorkflowEventMapper.Map(new UnknownEvent());

        mapping.IsKnown.ShouldBeFalse();
        mapping.Draft.ShouldBeNull();
    }

    private sealed class UnknownEvent() : WorkflowEvent(data: null);
}
