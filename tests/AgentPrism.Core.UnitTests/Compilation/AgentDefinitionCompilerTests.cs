using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class AgentDefinitionCompilerTests
{
    [Fact]
    public void Harness_ayari_yoksa_sade_sohbet_agenti_uretilir()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition());

        agent.ShouldBeOfType<ChatClientAgent>();
        agent.Name.ShouldBe("test-agent");
    }

    [Fact]
    public void Harness_ayari_varsa_harness_agenti_uretilir()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition(harness: new HarnessSettings
        {
            MaxContextWindowTokens = 4_096,
            DisableWebSearch = true,
        }));

        agent.ShouldBeOfType<HarnessAgent>();
    }

    [Fact]
    public void Model_ayarlari_sohbet_seceneklerine_aktarilir()
    {
        var client = new FakeChatClient();
        var provider = new FakeModelProvider(client);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = new ModelBinding
            {
                Provider = "fake",
                Model = "fake-model",
                Temperature = 0.3f,
                TopP = 0.9f,
                MaxOutputTokens = 512,
            },
        };

        compiler.Compile(definition);

        provider.LastBinding.ShouldNotBeNull();
        provider.LastBinding!.Temperature.ShouldBe(0.3f);
        provider.LastBinding.TopP.ShouldBe(0.9f);
        provider.LastBinding.MaxOutputTokens.ShouldBe(512);
    }

    [Fact]
    public void Kayitli_tool_adlari_cozulur()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"), TestData.Tool("search"));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), registry);

        var agent = compiler.Compile(TestData.Definition(toolNames: ["get_order", "search"]));

        agent.ShouldNotBeNull();
    }

    [Fact]
    public void Bilinmeyen_tool_adi_derlemeyi_durdurur()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), registry);

        var exception = Should.Throw<AgentPrismCompilationException>(
            () => compiler.Compile(TestData.Definition(toolNames: ["get_order", "silinmis_tool"])));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("silinmis_tool");
        // Hata mesaji kullaniciya ne yapacagini soylemeli.
        exception.Message.ShouldContain("get_order");
        exception.Message.ShouldContain("AddTool");
    }

    [Fact]
    public void Bilinmeyen_saglayici_derlemeyi_durdurur()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(), TestData.Registry());

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "yok-boyle") };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("yok-boyle");
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Null_tanim_reddedilir()
    {
        var compiler = CreateCompiler();

        Should.Throw<ArgumentNullException>(() => compiler.Compile(null!));
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());
}
