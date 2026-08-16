using System.Net;
using AgentPrism.Testing;

namespace AgentPrism.Testing.UnitTests;

public sealed class AgentPrismTestHostTests
{
    [Fact]
    public async Task Default_setup_starts_without_any_extra_configuration()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/agentprism/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responds_from_a_different_prefix_when_one_is_given()
    {
        await using var host = await AgentPrismTestHost.StartAsync(options => options.Prefix = "/panel");

        using var response = await host.Client.GetAsync("/panel/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisposeAsync_stops_the_application()
    {
        var host = await AgentPrismTestHost.StartAsync();
        var client = host.Client;

        await host.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => client.GetAsync("/agentprism/api/meta"));
    }

    [Fact]
    public async Task RunAsync_runs_a_registered_agent_and_returns_its_record()
    {
        await using var host = await AgentPrismTestHost.StartAsync(options =>
        {
            options.ModelProvider = new FakeModelProvider().EchoesUserMessage();
            options.ConfigureAgentPrism = builder => builder.AddAgent(new AgentDefinition
            {
                Name = "assistant",
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = options.ModelProvider.Name, Model = "fake-model" },
                Origin = AgentDefinitionOrigin.Code,
            });
        });

        var run = await host.RunAsync("assistant", "hello");

        run.ShouldHaveCompleted();
        run.Record.AgentName.ShouldBe("assistant");
    }
}
