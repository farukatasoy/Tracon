namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Tanim dogrulamasi. Ayni kurallar hem HTTP kayit ucunda hem derleyicide
/// calisir; bu yuzden kural tek yerde (AgentPrism.Core) yasar.
/// </summary>
public sealed class WorkflowDefinitionValidatorTests
{
    [Fact]
    public void Gecerli_tanim_kabul_edilir()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Sequential, ["a", "b"]))
            .ShouldBeNull();

    [Fact]
    public void Bos_agent_listesi_reddedilir()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Sequential, []))!
            .ShouldContain("hicbir agent icermiyor", Case.Sensitive);

    [Fact]
    public void Tekrar_eden_agent_adi_reddedilir()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Sequential, ["a", "b", "a"]))!
            .ShouldContain("birden fazla kez geciyor", Case.Sensitive);

    [Theory]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.Handoff)]
    [InlineData(WorkflowKind.GroupChat)]
    public void Iki_agent_isteyen_desenler_tek_agentle_reddedilir(WorkflowKind kind)
        => WorkflowDefinitionValidator
            .Validate(Definition(kind, ["a"]))!
            .ShouldContain("en az iki agent", Case.Sensitive);

    [Fact]
    public void Magentic_yonetici_agent_ister()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Magentic, ["a"]))!
            .ShouldContain("'managerAgentName' zorunludur", Case.Sensitive);

    [Fact]
    public void Magentic_yonetici_ayni_anda_katilimci_olamaz()
    {
        var definition = Definition(WorkflowKind.Magentic, ["a", "b"]) with { ManagerAgentName = "a" };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("hem yonetici hem katilimci", Case.Sensitive);
    }

    [Fact]
    public void GroupChat_yonetici_agent_kabul_etmez()
    {
        // GroupChat'in yoneticisi bir agent DEGILDIR: sirayi dagitan kod
        // tarafindaki round-robin yoneticisidir. Alani sessizce yok saymak,
        // kullanicinin bekledigi davranisin olusmadigini gizlerdi.
        var definition = Definition(WorkflowKind.GroupChat, ["a", "b"]) with { ManagerAgentName = "c" };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("yalnizca 'Magentic' desenine aittir", Case.Sensitive);
    }

    [Fact]
    public void Handoff_disinda_devretme_talimati_reddedilir()
    {
        var definition = Definition(WorkflowKind.Sequential, ["a", "b"]) with
        {
            HandoffInstructions = "gerekirse devret",
        };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("'handoffInstructions'", Case.Sensitive);
    }

    [Fact]
    public void Sifir_tur_siniri_reddedilir()
    {
        var definition = Definition(WorkflowKind.GroupChat, ["a", "b"]) with { MaxIterations = 0 };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("pozitif olmalidir", Case.Sensitive);
    }

    [Fact]
    public void Bilinmeyen_desen_reddedilir()
    {
        var definition = Definition((WorkflowKind)99, ["a", "b"]);

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("bilinmeyen bir desen", Case.Sensitive);
    }

    [Fact]
    public void Require_gecersiz_tanimda_istisna_atar()
        => Should.Throw<AgentPrismException>(
            () => WorkflowDefinitionValidator.Require(Definition(WorkflowKind.Sequential, [])));

    private static WorkflowDefinition Definition(WorkflowKind kind, IReadOnlyList<string> agentNames)
        => new()
        {
            Name = "test-workflow",
            Kind = kind,
            AgentNames = agentNames,
        };
}
