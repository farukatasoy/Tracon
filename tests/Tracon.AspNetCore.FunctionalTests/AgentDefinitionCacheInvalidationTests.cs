using System.Net;
using System.Net.Http.Json;
using Tracon.Testing;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The compiled-agent cache must never serve a definition that no longer exists.
/// </summary>
public sealed class AgentDefinitionCacheInvalidationTests
{
    private const string AgentName = "recycled-agent";
    private const string ModelName = "cache-model";

    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);
    private static readonly Uri AgentUri = new($"/tracon/api/agents/{AgentName}", UriKind.Relative);

    [Fact]
    public async Task Recreating_a_deleted_name_runs_the_new_definition()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAsync(host, "first");
        await RunAsync(host);
        provider.Requests[^1].Options!.Instructions.ShouldBe("first");

        using (var deleted = await host.Client.DeleteAsync(AgentUri, TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateAsync(host, "second");
        await RunAsync(host);

        provider.Requests[^1].Options!.Instructions.ShouldBe("second");
    }


    [Fact]
    public async Task Recreating_a_deleted_shared_instructions_block_reaches_the_agent_that_reads_it()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAsync(host, "BLOCK-ONE", name: "house-rules");
        await CreateAsync(host, "own text", name: "reader-agent", sharedInstructionsName: "house-rules");

        await RunAsync(host, "reader-agent");
        provider.Requests[^1].Options!.Instructions!.ShouldContain("BLOCK-ONE");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/agents/house-rules", UriKind.Relative),
            TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateAsync(host, "BLOCK-TWO", name: "house-rules");
        await RunAsync(host, "reader-agent");

        provider.Requests[^1].Options!.Instructions!.ShouldContain("BLOCK-TWO");
    }

    [Fact]
    public async Task Recreating_a_deleted_sub_agent_reaches_the_agent_that_calls_it()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await CreateAsync(host, "helper text", name: "helper", description: "DESC-ONE");
        await CreateAsync(host, "own text", name: "caller-agent", callableAgentNames: ["helper"]);

        await RunAsync(host, "caller-agent");
        provider.Requests[^1].Options!.Instructions!.ShouldContain("DESC-ONE");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/agents/helper", UriKind.Relative),
            TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await CreateAsync(host, "helper text", name: "helper", description: "DESC-TWO");
        await RunAsync(host, "caller-agent");

        provider.Requests[^1].Options!.Instructions!.ShouldContain("DESC-TWO");
    }

    private static async Task CreateAsync(
        Infrastructure.TraconTestHost host,
        string instructions,
        string name = AgentName,
        string? description = null,
        string? sharedInstructionsName = null,
        IReadOnlyList<string>? callableAgentNames = null)
    {
        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            new AgentDefinitionRequest
            {
                Name = name,
                Description = description,
                Instructions = instructions,
                SharedInstructionsName = sharedInstructionsName,
                CallableAgentNames = callableAgentNames ?? [],
                Model = new ModelBinding { Provider = "cache-provider", Model = ModelName },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task RunAsync(Infrastructure.TraconTestHost host, string name = AgentName)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{name}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hi" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("cache-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<Infrastructure.TraconTestHost> StartAsync(FakeModelProvider provider)
        => Infrastructure.TraconTestHost.StartAsync(configureTracon: builder => builder.AddModelProvider(provider));
}
