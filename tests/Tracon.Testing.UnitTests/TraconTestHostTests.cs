using System.Net;
using Tracon.Testing;

namespace Tracon.Testing.UnitTests;

public sealed class TraconTestHostTests
{
    [Fact]
    public async Task Default_setup_starts_without_any_extra_configuration()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/tracon/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responds_from_a_different_prefix_when_one_is_given()
    {
        await using var host = await TraconTestHost.StartAsync(options => options.Prefix = "/panel");

        using var response = await host.Client.GetAsync("/panel/api/meta");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisposeAsync_stops_the_application()
    {
        var host = await TraconTestHost.StartAsync();
        var client = host.Client;

        await host.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => client.GetAsync("/tracon/api/meta"));
    }

    [Fact]
    public async Task RunAsync_runs_a_registered_agent_and_returns_its_record()
    {
        await using var host = await TraconTestHost.StartAsync(options =>
        {
            options.ModelProvider = new FakeModelProvider().EchoesUserMessage();
            options.ConfigureTracon = builder => builder.AddAgent(new AgentDefinition
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
