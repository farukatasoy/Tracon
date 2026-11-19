using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A provider set up with the configuration keys of its key and its address sends the value that is current when a call
/// starts, and the host never restarts.
/// </summary>
/// <remarks>
/// <para>
/// Crosses the boundaries a unit test cannot: the agent is compiled once and cached, so the client it holds was built
/// before the rotation; the provider is the real OpenAI client in front of a local HTTP server, so the key and the host
/// that count are the ones on the wire.
/// </para>
/// <para>
/// The configuration is changed in memory and the next call reads it. A real file source that reloads is the manual case
/// of the phase; no test waits for a file watcher.
/// </para>
/// </remarks>
public sealed class LiveSetupKeyRotationTests
{
    private const string AgentName = "live-setup-agent";
    private const string KeyName = "Platform:OpenAI:Key";
    private const string EndpointName = "Platform:OpenAI:Endpoint";

    [Fact]
    public async Task A_rotated_setup_key_is_the_key_of_the_next_run_of_the_same_compiled_agent()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "key-one"));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = server.BaseAddress;
                options.ApiKey = "startup-key";
                options.ApiKeyConfigurationName = KeyName;
            });

        await RunAsync(host);

        configuration[KeyName] = "key-two";

        await RunAsync(host);

        // The name has no value for a moment (a source that reloads reads an empty file): the last valid key is used.
        configuration[KeyName] = "  ";

        await RunAsync(host);

        configuration[KeyName] = "key-three";

        await RunAsync(host);

        // The key is read when a call starts: the agent was compiled and cached after the first run, and every later run
        // was still sent with the key the name held at that moment.
        server.ReceivedAuthorizationHeaders.ShouldBe(["Bearer key-one", "Bearer key-two", "Bearer key-two", "Bearer key-three"]);
    }

    [Fact]
    public async Task A_provider_that_names_no_configuration_key_keeps_the_key_it_started_with()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "ignored-because-no-name-is-set"));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = server.BaseAddress;
                options.ApiKey = "startup-key";
            });

        await RunAsync(host);

        configuration[KeyName] = "still-ignored";

        await RunAsync(host);

        server.ReceivedAuthorizationHeaders.ShouldBe(["Bearer startup-key", "Bearer startup-key"]);
    }

    [Fact]
    public async Task A_changed_setup_address_is_where_the_next_run_goes_and_the_key_goes_with_it()
    {
        await using var first = await FakeOpenAiCompatibleServer.StartAsync();
        await using var second = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "key-one"), (EndpointName, first.BaseAddress.ToString()));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = first.BaseAddress;
                options.ApiKey = "startup-key";
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        await RunAsync(host);

        configuration[KeyName] = "key-two";
        configuration[EndpointName] = second.BaseAddress.ToString();

        await RunAsync(host);

        // The address is not absolute: the last valid address stays, the key is still the new one.
        configuration[EndpointName] = "not an address";
        configuration[KeyName] = "key-three";

        await RunAsync(host);

        first.ReceivedAuthorizationHeaders.ShouldBe(["Bearer key-one"]);
        second.ReceivedAuthorizationHeaders.ShouldBe(["Bearer key-two", "Bearer key-three"]);
    }

    [Fact]
    public async Task An_unusable_value_warns_once_with_the_name_and_never_the_value()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "key-one"));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = server.BaseAddress;
                options.ApiKeyConfigurationName = KeyName;
            });

        await RunAsync(host);

        configuration[KeyName] = "key\nwith a line break";

        await RunAsync(host);
        await RunAsync(host);
        await RunAsync(host);

        var warnings = host.Logs.Entries.Where(static entry => entry.Contains(KeyName, StringComparison.Ordinal)).ToList();

        warnings.Count.ShouldBe(1, string.Join('\n', warnings));
        warnings[0].ShouldContain("last valid");
        host.Logs.AllText.ShouldNotContain("with a line break");
        server.ReceivedAuthorizationHeaders.ShouldBe(["Bearer key-one", "Bearer key-one", "Bearer key-one", "Bearer key-one"]);
    }

    [Fact]
    public async Task The_setup_client_a_judge_and_a_summarizer_take_follows_the_rotation_too()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "key-one"));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = server.BaseAddress;
                options.ApiKeyConfigurationName = KeyName;
            });

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();
        var binding = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "fake-local-model" };

        // A judge, a loop evaluator and the context summarizer build their client with the setup credential, once, and keep
        // it. The one built here is held across the rotation.
        using var judge = await registry.CreateSetupChatClientAsync(binding, TestContext.Current.CancellationToken);

        await judge.GetResponseAsync([new ChatMessage(ChatRole.User, "judge this")], cancellationToken: TestContext.Current.CancellationToken);

        configuration[KeyName] = "key-two";

        await judge.GetResponseAsync([new ChatMessage(ChatRole.User, "judge this again")], cancellationToken: TestContext.Current.CancellationToken);

        server.ReceivedAuthorizationHeaders.ShouldBe(["Bearer key-one", "Bearer key-two"]);
    }

    [Fact]
    public async Task The_model_health_check_uses_the_key_that_is_current_when_it_runs()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "key-one"));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = server.BaseAddress;
                options.ApiKeyConfigurationName = KeyName;
            });

        using (await host.Client.GetAsync(new Uri("/tracon/api/models/health/openai?refresh=true", UriKind.Relative), TestContext.Current.CancellationToken))
        {
        }

        configuration[KeyName] = "key-two";

        using (await host.Client.GetAsync(new Uri("/tracon/api/models/health/openai?refresh=true", UriKind.Relative), TestContext.Current.CancellationToken))
        {
        }

        server.ModelsAuthorizationHeaders.ShouldBe(["Bearer key-one", "Bearer key-two"]);
    }

    [Fact]
    public async Task A_failure_that_quotes_the_rotated_key_is_masked_and_so_is_the_one_it_replaced()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        var configuration = Configuration((KeyName, "canary-old-key-1111aaaa"));

        await using var host = await StartAsync(
            configuration,
            options =>
            {
                options.Endpoint = server.BaseAddress;
                options.ApiKeyConfigurationName = KeyName;
            });

        await RunAsync(host);

        configuration[KeyName] = "canary-new-key-2222bbbb";

        // The service refuses the new key and says which key it refused.
        server.ChatCompletionOverride = async (context, _) =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Incorrect API key provided: canary-new-key-2222bbbb.", context.RequestAborted);
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = Guid.NewGuid().ToString("N") }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        body.ShouldNotContain("canary-new-key-2222bbbb");
        body.ShouldNotContain("canary-old-key-1111aaaa");
        host.Logs.AllText.ShouldNotContain("canary-new-key-2222bbbb");
        host.Logs.AllText.ShouldNotContain("canary-old-key-1111aaaa");
    }

    private static IConfiguration Configuration(params (string Name, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static Task<TraconTestHost> StartAsync(IConfiguration configuration, Action<OpenAIProviderOptions> configure)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseOpenAI(configure)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "fake-local-model" },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services => services.AddSingleton(configuration));

    private static async Task RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = Guid.NewGuid().ToString("N") }),
        };

        // The Idempotency-Key header selects the single JSON response.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
