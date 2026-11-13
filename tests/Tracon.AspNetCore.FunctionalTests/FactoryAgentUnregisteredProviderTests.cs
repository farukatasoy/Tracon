using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A factory agent that declares a provider nobody registered is refused as a definition agent with the same binding is
/// refused: the same exception type, the agent's name in the text, and over A2A the protocol's own error.
/// </summary>
/// <remarks>
/// The declared model is built by the registry before the factory runs. Its failure used to leave the catalog as it came
/// out of the registry, a plain <see cref="TraconException"/> whose text names no agent, so a mistyped provider name was
/// the one compile failure of a factory agent that the A2A and MCP surfaces answered with the library's bare "internal
/// error" and that a caller could not tell from a passing fault.
/// </remarks>
public sealed class FactoryAgentUnregisteredProviderTests
{
    private const string FactoryAgentName = "factory-ghost";
    private const string DefinitionAgentName = "definition-ghost";

    private static readonly ModelBinding Typo = new() { Provider = "typo-provider", Model = "m-1" };

    [Fact]
    public async Task The_catalog_refuses_both_kinds_of_agent_with_the_same_exception_type_and_the_agent_name()
    {
        await using var host = await StartAsync();

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var definition = await Should.ThrowAsync<TraconCompilationException>(async () =>
            await catalog.ResolveAsync(DefinitionAgentName, culture: null, TestContext.Current.CancellationToken));
        var factory = await Should.ThrowAsync<TraconCompilationException>(async () =>
            await catalog.ResolveAsync(FactoryAgentName, culture: null, TestContext.Current.CancellationToken));

        factory.AgentName.ShouldBe(FactoryAgentName);
        factory.Message.ShouldStartWith($"Agent '{FactoryAgentName}' could not be compiled: ", Case.Sensitive);
        factory.Message.ShouldContain("No model provider named 'typo-provider' is registered", Case.Sensitive);
        definition.Message.ShouldContain("No model provider named 'typo-provider' is registered", Case.Sensitive);
    }

    [Fact]
    public async Task A_run_over_HTTP_answers_a_compilation_failure_that_names_the_agent()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{FactoryAgentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = "hi" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("title").GetString().ShouldBe("Agent compilation failed");
        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain($"Agent '{FactoryAgentName}'", Case.Sensitive);
    }

    [Fact]
    public async Task A2A_answers_the_text_a_definition_agent_gets_and_not_the_libraries_bare_internal_error()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/a2a/{FactoryAgentName}", UriKind.Relative),
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "hi" } }, messageId = Guid.NewGuid().ToString() } },
            },
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain($"Agent '{FactoryAgentName}' cannot be run", Case.Sensitive);
        body.ShouldContain("a retry will not succeed", Case.Sensitive);
        body.ShouldNotContain("An internal error occurred", Case.Sensitive);
    }

    private static async Task<TraconTestHost> StartAsync()
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(
                    FactoryAgentName,
                    Typo,
                    static context => context.ChatClient.AsAIAgent(instructions: "Answer briefly.", name: context.AgentName))
                .UseA2A(static options => options.ExposedAgents.Add(FactoryAgentName)),
            configureAfterMap: static app => app.MapTraconA2A());

        // Saved straight to the store: the HTTP endpoint would refuse the unregistered provider, and the point is the
        // catalog's answer to one that got there anyway (a provider package removed after the agent was saved).
        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(
            new AgentDefinition
            {
                Name = DefinitionAgentName,
                Instructions = "Answer briefly.",
                Model = Typo,
                Origin = AgentDefinitionOrigin.Database,
            },
            TestContext.Current.CancellationToken);

        return host;
    }
}
