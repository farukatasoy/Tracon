using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using AgentPrism.Testing;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Culture-keyed instructions (F-117, phase 72): the <c>culture</c> field on
/// <see cref="AgentRunRequest"/> selects which of
/// <see cref="AgentDefinition.InstructionsByCulture"/> reaches the model.
/// </summary>
public sealed class CultureInstructionEndpointTests
{
    private const string AgentName = "polyglot-agent";
    private const string ModelName = "polyglot-model";
    private const string DefaultInstructions = "Reply briefly.";
    private const string AlternateCultureInstructions = "Answer using the tr culture text.";

    private static readonly Uri RunUri = new($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task No_culture_uses_the_default_instructions()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hi" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(DefaultInstructions);
    }

    [Fact]
    public async Task Matching_culture_selects_its_own_instructions()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hi", Culture = "tr" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(AlternateCultureInstructions);
    }

    [Fact]
    public async Task Region_subtag_falls_back_to_its_parent_culture()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hi", Culture = "tr-TR" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(AlternateCultureInstructions);
    }

    [Fact]
    public async Task Unmatched_culture_falls_back_to_the_default_instructions()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hi", Culture = "de" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(DefaultInstructions);
    }

    [Fact]
    public async Task Accept_Language_header_is_ignored()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Accept-Language", "tr");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(DefaultInstructions);
    }

    [Fact]
    public async Task Back_to_back_runs_in_different_cultures_do_not_share_the_cached_compiled_agent()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var trResponse = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hi", Culture = "tr" }, TestContext.Current.CancellationToken);
        trResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(AlternateCultureInstructions);

        using var enResponse = await host.Client.PostAsJsonAsync(
            RunUri, new AgentRunRequest { Message = "hi" }, TestContext.Current.CancellationToken);
        enResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe(DefaultInstructions);
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("polyglot-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<Infrastructure.AgentPrismTestHost> StartAsync(FakeModelProvider provider)
        => Infrastructure.AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = DefaultInstructions,
                    InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["tr"] = AlternateCultureInstructions,
                    },
                    Model = new ModelBinding { Provider = "polyglot-provider", Model = ModelName },
                    Origin = AgentDefinitionOrigin.Code,
                })
                .AddModelProvider(provider));
}
