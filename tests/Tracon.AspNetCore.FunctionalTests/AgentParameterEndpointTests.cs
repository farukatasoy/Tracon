using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The parameterized agent input surface (Phase 86, F-34): a definition's
/// <see cref="AgentDefinition.Parameters"/> schema, and the values a run
/// request supplies for it.
/// </summary>
public sealed class AgentParameterEndpointTests
{
    private const string AgentName = "billing-agent";
    private const string ModelName = "param-model";

    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);
    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri EstimateUri = new($"/tracon/api/agents/{AgentName}/estimate", UriKind.Relative);

    [Fact]
    public async Task Run_with_all_required_parameters_binds_them_into_the_instructions()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        await CreateParameterizedAgentAsync(host);

        using var response = await PostBufferedAsync(
            host,
            RunUri,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests[^1].Options!.Instructions.ShouldBe("Hello Acme, how can I help?");
    }

    [Fact]
    public async Task Run_missing_a_required_parameter_does_not_start_and_names_it()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        await CreateParameterizedAgentAsync(host);

        using var response = await PostBufferedAsync(host, RunUri, new AgentRunRequest { Message = "hi" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await Infrastructure.TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("missingParameters").EnumerateArray()
            .Select(static element => element.GetString())
            .ShouldContain("customer", StringComparer.Ordinal);

        // No call reached the provider: the run truly did not start.
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Estimate_with_the_same_missing_parameter_returns_the_identical_error()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        await CreateParameterizedAgentAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            EstimateUri, new AgentRunRequest { Message = "hi" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await Infrastructure.TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("missingParameters").EnumerateArray()
            .Select(static element => element.GetString())
            .ShouldContain("customer", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Run_with_an_unknown_parameter_is_rejected_not_silently_dropped()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        await CreateParameterizedAgentAsync(host);

        using var response = await PostBufferedAsync(
            host,
            RunUri,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customer"] = "Acme",
                    ["typo"] = "oops",
                },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await Infrastructure.TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("unknownParameters").EnumerateArray()
            .Select(static element => element.GetString())
            .ShouldContain("typo", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Run_with_a_value_over_the_configured_limit_does_not_start_and_names_it()
    {
        var provider = Provider();
        await using var host = await Infrastructure.TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddModelProvider(provider),
            configureServices: services => services.Configure<TraconOptions>(
                options => options.MaxParameterValueLength = 4));
        await CreateParameterizedAgentAsync(host);

        using var response = await PostBufferedAsync(
            host,
            RunUri,
            new AgentRunRequest
            {
                Message = "hi",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme Corp" },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await Infrastructure.TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("tooLongParameters").EnumerateArray()
            .Select(static element => element.GetString())
            .ShouldContain("customer", StringComparer.Ordinal);

        // No call reached the provider: the run truly did not start.
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_placeholder_is_rejected_only_once_the_definition_declares_a_schema()
    {
        // An empty Parameters list means the agent never opted into this
        // feature: "{{...}}" stays plain, coincidental text - the same as
        // before this feature existed. The check only turns on once at
        // least one parameter is declared (An_undeclared_placeholder_... below).
        await using var host = await StartAsync(Provider());

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            new AgentDefinitionRequest
            {
                Name = "curly-braces-agent",
                Instructions = "Hello {{customer}}.",
                Parameters = [],
                Model = new ModelBinding { Provider = "param-provider", Model = ModelName },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_undeclared_placeholder_fails_at_create_time_once_a_schema_exists()
    {
        await using var host = await StartAsync(Provider());

        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            new AgentDefinitionRequest
            {
                Name = "broken-agent",
                Instructions = "Hello {{customer}}.",
                // "tone" is declared, but the text references "customer" - undeclared.
                Parameters = [new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text }],
                Model = new ModelBinding { Provider = "param-provider", Model = ModelName },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_agent_with_no_parameter_schema_is_unaffected()
    {
        // The overwhelming majority of agents: no schema, no gate cost, no
        // behavior change from before this feature existed.
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using (var created = await host.Client.PostAsJsonAsync(
            Agents,
            new AgentDefinitionRequest
            {
                Name = "plain-agent",
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = "param-provider", Model = ModelName },
            },
            TestContext.Current.CancellationToken))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/plain-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hi" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task CreateParameterizedAgentAsync(Infrastructure.TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            Agents,
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Hello {{customer}}, how can I help?",
                Parameters =
                [
                    new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                ],
                Model = new ModelBinding { Provider = "param-provider", Model = ModelName },
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static FakeModelProvider Provider()
        => new FakeModelProvider("param-provider")
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<Infrastructure.TraconTestHost> StartAsync(FakeModelProvider provider)
        => Infrastructure.TraconTestHost.StartAsync(configureTracon: builder => builder.AddModelProvider(provider));

    /// <summary>Enters the non-streaming branch so a 400/200 status code is directly observable.</summary>
    private static async Task<HttpResponseMessage> PostBufferedAsync(Infrastructure.TraconTestHost host, Uri uri, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
