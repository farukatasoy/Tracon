using AgentPrism.Workflows.UnitTests.Fakes;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Tanimdan graf derlemesi. Bes desenin tamami gercekten kurulabilmelidir;
/// Microsoft Agent Framework'un builder API'si tahmin edilemez ve bir desenin
/// kurulmadigi ancak calistirma aninda anlasilirdi.
/// </summary>
public sealed class WorkflowDefinitionCompilerTests
{
    [Fact]
    public async Task Sequential_derlenir()
    {
        var host = new WorkflowTestHost("yazar", "editor", "kontrol");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor", "kontrol"],
        });

        workflow.Name.ShouldBe("zincir");

        // Uc agent + cikti toplayicisi. MAF hazir desenin sonuna kendi
        // toplayici executor'unu ekler; sayi bu yuzden agent sayisindan buyuktur.
        workflow.ReflectExecutors().Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Concurrent_derlenir()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "paralel",
            Kind = WorkflowKind.Concurrent,
            AgentNames = ["yazar", "editor"],
        });

        workflow.Name.ShouldBe("paralel");
    }

    [Fact]
    public async Task Handoff_derlenir()
    {
        var host = new WorkflowTestHost("destek", "uzman");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "devret",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["destek", "uzman"],
            HandoffInstructions = "Teknik soru gelirse uzmana devret.",
            MaxIterations = 4,
        });

        workflow.Name.ShouldBe("devret");
    }

    [Fact]
    public async Task GroupChat_derlenir()
    {
        var host = new WorkflowTestHost("yazar", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "sohbet",
            Kind = WorkflowKind.GroupChat,
            AgentNames = ["yazar", "editor"],
            MaxIterations = 2,
        });

        workflow.Name.ShouldBe("sohbet");
    }

    [Fact]
    public async Task Magentic_derlenir()
    {
        var host = new WorkflowTestHost("yonetici", "yazar", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["yazar", "editor"],
            ManagerAgentName = "yonetici",
            MaxIterations = 2,
        });

        workflow.Name.ShouldBe("magentic");
    }

    [Fact]
    public async Task Ayni_tanim_iki_kez_derlenirse_EXECUTOR_KIMLIKLERI_AYNI_KALIR()
    {
        // 🚨 Kontrol noktasindan sürdürme buna baglidir. Microsoft Agent
        // Framework executor kimliklerini agent ORNEGINDEN turetir; kimlikler
        // her derlemede degisirse MAF kontrol noktasini reddeder
        // ("The specified checkpoint is not compatible with the workflow").
        // Olculdu (Faz 15): yeni agent ornekleriyle kurulan graf uyumsuz cikti.
        var host = new WorkflowTestHost("yazar", "editor");

        var definition = new WorkflowDefinition
        {
            Name = "zincir",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["yazar", "editor"],
        };

        var first = await host.Compiler.CompileAsync(definition);
        var second = await host.Compiler.CompileAsync(definition);

        first.ReflectExecutors().Keys.Order(StringComparer.Ordinal)
            .ShouldBe(second.ReflectExecutors().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Bilinmeyen_agent_adi_anlasilir_hata_verir()
    {
        var host = new WorkflowTestHost("yazar");

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "zincir",
                Kind = WorkflowKind.Sequential,
                AgentNames = ["yazar", "olmayan-agent"],
            }));

        exception.Message.ShouldContain("'olmayan-agent'", Case.Sensitive);
        exception.Message.ShouldContain("katalogda yok", Case.Sensitive);
    }

    [Fact]
    public async Task Gecersiz_tanim_derlenmeden_reddedilir()
    {
        var host = new WorkflowTestHost("yazar");

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "zincir",
                Kind = WorkflowKind.Sequential,
                AgentNames = [],
            }));

        exception.Message.ShouldContain("hicbir agent icermiyor", Case.Sensitive);
    }
}
