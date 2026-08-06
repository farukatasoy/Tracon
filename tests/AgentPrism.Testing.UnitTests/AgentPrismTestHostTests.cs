using System.Net;
using AgentPrism.Testing;

namespace AgentPrism.Testing.UnitTests;

public sealed class AgentPrismTestHostTests
{
    [Fact]
    public async Task Varsayilan_kurulum_hicbir_ek_yapilandirma_olmadan_ayaga_kalkar()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/agentprism/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Farkli_bir_onek_verilirse_o_onekten_yanit_verir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(options => options.Prefix = "/panel");

        using var response = await host.Client.GetAsync("/panel/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisposeAsync_uygulamayi_durdurur()
    {
        var host = await AgentPrismTestHost.StartAsync();
        var client = host.Client;

        await host.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => client.GetAsync("/agentprism/api/meta"));
    }

    [Fact]
    public async Task RunAsync_kayitli_bir_agenti_calistirir_ve_kaydini_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(options =>
        {
            options.ModelProvider = new FakeModelProvider().EchoesUserMessage();
            options.ConfigureAgentPrism = builder => builder.AddAgent(new AgentDefinition
            {
                Name = "yardimci",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = options.ModelProvider.Name, Model = "fake-model" },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var run = await host.RunAsync("yardimci", "merhaba");

        run.ShouldHaveCompleted();
        run.Record.AgentName.ShouldBe("yardimci");
    }
}
