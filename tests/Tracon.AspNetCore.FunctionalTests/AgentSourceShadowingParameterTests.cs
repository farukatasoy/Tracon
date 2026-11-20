using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A custom agent source that outranks the definition store, for an agent name
/// the store also holds with a parameter schema.
/// </summary>
/// <remarks>
/// <para>
/// The catalog resolves a name from the source with the lower
/// <see cref="IAgentSource.Priority"/> value, and <c>/run</c> is documented to
/// run that source's agent. The run endpoint looked the parameter schema up in
/// the code registrations and then the store, never in the sources. A store
/// definition with a schema therefore pulled the run away from the winning
/// source: the endpoint compiled and ran the store definition, or refused the
/// run for want of the store schema's values. Setting no value at all was
/// enough to trigger it.
/// </para>
/// <para>
/// Both cases go through the real host: the schema read, the refusal and the
/// run are three different layers.
/// </para>
/// </remarks>
public sealed class AgentSourceShadowingParameterTests
{
    private const string AgentName = "shadowed-agent";
    private const string ModelName = "shadow-model";
    private const string ProviderName = "shadow-provider";
    private const string SourceInstructions = "The custom source answers.";

    [Fact]
    public async Task A_run_without_values_runs_the_source_that_outranks_the_stored_definition()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRunAsync(host, new AgentRunRequest { Message = "hi" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        provider.Requests[^1].Options!.Instructions.ShouldBe(SourceInstructions);
    }

    [Fact]
    public async Task A_value_for_a_name_only_the_stored_schema_declares_is_refused_as_unknown()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRunAsync(
            host,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var unknown = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("unknownParameters");

        unknown.EnumerateArray().Select(static element => element.GetString()).ShouldContain("customer", StringComparer.Ordinal);
        provider.Requests.ShouldBeEmpty();
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static async Task<TraconTestHost> StartAsync(FakeModelProvider provider)
    {
        var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgentSource(new OutrankingSource(provider));
            });

        // Saved straight into the store: the management API refuses a name a
        // source already lists, and the point here is a definition the source
        // came to outrank.
        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(
            new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Hello {{customer}}, how can I help?",
                Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true }],
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            });

        return host;
    }

    private static async Task<HttpResponseMessage> PostRunAsync(TraconTestHost host, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }

    /// <summary>
    /// An opaque source: it returns its own agent and declares no parameters.
    /// Its priority is lower than the store's, so the catalog serves its agent.
    /// </summary>
    private sealed class OutrankingSource(FakeModelProvider provider) : IAgentSource
    {
        public string Name => "outranking-source";

        public int Priority => 50;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor { Name = AgentName, Origin = AgentDefinitionOrigin.Custom, SourceName = Name },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new(string.Equals(agentName, AgentName, StringComparison.Ordinal)
                ? new ChatClientAgent(
                    provider.CreateChatClient(new ModelBinding { Provider = ProviderName, Model = ModelName }),
                    new ChatClientAgentOptions
                    {
                        Name = AgentName,
                        ChatOptions = new ChatOptions { Instructions = SourceInstructions },
                    })
                : null);
    }
}
