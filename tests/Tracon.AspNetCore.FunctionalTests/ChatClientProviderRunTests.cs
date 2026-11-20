using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent bound to a provider that <c>AddChatClientProvider</c> registered, through the real host.
/// </summary>
/// <remarks>
/// The unit tests prove the registration and the refusal of a client that runs its own tool-call loop. This is the
/// path they cannot reach: a definition names the provider, the registry builds the shared pipeline above the client,
/// and the run answers over HTTP.
/// </remarks>
public sealed class ChatClientProviderRunTests
{
    private const string ProviderName = "bring-your-own";
    private const string ModelName = "own-model";

    [Fact]
    public async Task An_agent_bound_to_a_registered_client_answers_through_it()
    {
        var inner = new FakeModelProvider("inner").WithModel(new ModelDescriptor { Name = ModelName }).EchoesUserMessage();
        var client = inner.CreateChatClient(new ModelBinding { Provider = "inner", Model = ModelName });

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddChatClientProvider(ProviderName, client, [new ModelDescriptor { Name = ModelName }])
            .AddAgent(Agent("Mixed-Case-Agent", ProviderName)));

        (await RunAsync(host, "Mixed-Case-Agent")).ShouldContain("ping");
        inner.Requests.Count.ShouldBe(1, "the client the host registered served the run");
    }

    [Fact]
    public async Task The_provider_name_is_matched_ignoring_letter_case()
    {
        var inner = new FakeModelProvider("inner").WithModel(new ModelDescriptor { Name = ModelName }).EchoesUserMessage();
        var client = inner.CreateChatClient(new ModelBinding { Provider = "inner", Model = ModelName });

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddChatClientProvider(ProviderName, client)
            .AddAgent(Agent("shouting-agent", "BRING-YOUR-OWN")));

        (await RunAsync(host, "shouting-agent")).ShouldContain("ping");
    }

    [Fact]
    public async Task A_factory_client_is_built_from_the_binding_the_agent_names()
    {
        var inner = new FakeModelProvider("inner").WithModel(new ModelDescriptor { Name = ModelName }).EchoesUserMessage();
        var bindings = new List<string>();

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddChatClientProvider(
                ProviderName,
                (_, binding) =>
                {
                    lock (bindings)
                    {
                        bindings.Add(binding.Model);
                    }

                    return inner.CreateChatClient(new ModelBinding { Provider = "inner", Model = binding.Model });
                })
            .AddAgent(Agent("factory-agent", ProviderName)));

        (await RunAsync(host, "factory-agent")).ShouldContain("ping");

        lock (bindings)
        {
            bindings.ShouldContain(ModelName, StringComparer.Ordinal);
        }
    }

    private static AgentDefinition Agent(string name, string provider) => new()
    {
        Name = name,
        Instructions = "Answer briefly.",
        Model = new ModelBinding { Provider = provider, Model = ModelName },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static async Task<string> RunAsync(TraconTestHost host, string agent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agent}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "ping" }),
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(response);

        return string.Concat(body.GetProperty("response").GetProperty("messages").EnumerateArray()
            .SelectMany(static message => message.GetProperty("contents").EnumerateArray())
            .Select(static content => content.TryGetProperty("text", out var text) ? text.GetString() : null));
    }
}
