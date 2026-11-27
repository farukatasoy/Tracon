using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;
using Tracon.Tests.Common;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// The host's own image call (<see cref="IImageGenerationService"/>) over the registered Azure OpenAI client and a real socket:
/// what the call's settings put on the wire, and how often a failing call reaches the server.
/// </summary>
/// <remarks>
/// <para>
/// Azure OpenAI has no option for how often the client sends a call again: its client sends a failed call up to three times
/// more. The ceiling of the image options is the one way to ask for fewer, and it must not touch the chat client that shares
/// the pipeline.
/// </para>
/// <para>
/// The server answers every request and counts them, so a number here is what the resource would have billed for. The wait
/// between two tries runs on a <see cref="ManualClock"/> the host registered: nothing waits for a backoff.
/// </para>
/// </remarks>
public sealed class AzureOpenAIImageServiceTests
{
    private const string Answer = """{"created":1,"data":[{"b64_json":"iVBORw0KGgoAAAAA"}]}""";

    [Fact]
    public async Task A_call_setting_is_the_quality_on_the_wire_for_that_call_and_the_registered_one_returns_on_the_next()
    {
        await using var server = StubHttpServer.RespondToEvery(200, "OK", Answer);
        await using var provider = Build(server, new ManualClock(), registered => registered.ProviderSettings["azure-openai.quality"] = Text("medium"));
        var images = provider.GetRequiredService<IImageGenerationService>();

        await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a red kite", ProviderSettings = { ["azure-openai.quality"] = Text("high") } },
            TestContext.Current.CancellationToken);
        await images.GenerateAsync(new TraconImageRequest { Prompt = "a red kite" }, TestContext.Current.CancellationToken);

        var bodies = server.RequestBodies;

        bodies.Count.ShouldBe(2);
        bodies[0].ShouldContain("\"quality\":\"high\"");
        bodies[1].ShouldContain("\"quality\":\"medium\"");
    }

    [Fact]
    public async Task The_quality_key_of_another_provider_is_refused_and_sends_no_request()
    {
        await using var server = StubHttpServer.RespondToEvery(200, "OK", Answer);
        await using var provider = Build(server, new ManualClock());

        await Should.ThrowAsync<TraconException>(
            async () => await provider.GetRequiredService<IImageGenerationService>().GenerateAsync(
                new TraconImageRequest { Prompt = "a red kite", ProviderSettings = { ["openai.quality"] = Text("high") } },
                TestContext.Current.CancellationToken));

        server.Requests.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    public async Task A_failing_image_call_reaches_the_server_once_and_then_as_often_as_the_ceiling_of_the_provider_allows(int? ceiling, int expectedRequests)
    {
        await using var server = StubHttpServer.RespondToEvery(503, "Service Unavailable", "{}");
        var clock = new ManualClock();
        await using var provider = Build(server, clock, registered => registered.MaxRetries = ceiling);

        await FailAsync(provider, clock);

        server.Requests.ShouldBe(expectedRequests);
    }

    [Theory]
    [InlineData(429, "Too Many Requests")]
    [InlineData(500, "Internal Server Error")]
    [InlineData(503, "Service Unavailable")]
    public async Task A_ceiling_of_zero_sends_the_call_once_whatever_the_status_the_client_would_have_repeated(int status, string reason)
    {
        await using var server = StubHttpServer.RespondToEvery(status, reason, "{}");
        var clock = new ManualClock();
        await using var provider = Build(server, clock, registered => registered.MaxRetries = 0);

        await FailAsync(provider, clock);

        server.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task A_chat_call_on_the_same_client_is_still_sent_as_often_as_the_client_allows()
    {
        await using var server = StubHttpServer.RespondToEvery(503, "Service Unavailable", "{}");
        var clock = new ManualClock();
        await using var provider = Build(server, clock, registered => registered.MaxRetries = 0);

        await FailAsync(provider, clock);

        server.Requests.ShouldBe(1, "the image call is capped");

        using var chat = provider.GetRequiredService<IModelProviderRegistry>().CreateChatClient(TestData.Binding());
        var call = chat.GetResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        await Should.ThrowAsync<Exception>(() => clock.AdvanceUntilAsync(call));

        server.Requests.ShouldBe(1 + 4, "the chat call is the first try and the client's three retries: the ceiling is the image call's alone");
    }

    [Fact]
    public async Task The_ceiling_binds_from_configuration()
    {
        await using var server = StubHttpServer.RespondToEvery(503, "Service Unavailable", "{}");
        var clock = new ManualClock();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Images:Providers:azure-openai:MaxRetries"] = "0" })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTracon(configuration.GetSection("Tracon"))
            .UseAzureOpenAI(options =>
            {
                options.ApiKey = TestData.ApiKey;
                options.Endpoint = server.BaseAddress;
            })
            .UseAzureOpenAIImages(provider => provider.Model = "image-deployment");

        await using var host = services.BuildServiceProvider();

        await FailAsync(host, clock);

        server.Requests.ShouldBe(1, "the deployment was set in code and the ceiling in configuration: both reached the same entry");
    }

    private static async Task FailAsync(ServiceProvider provider, ManualClock clock)
    {
        var call = provider.GetRequiredService<IImageGenerationService>()
            .GenerateAsync(new TraconImageRequest { Prompt = "a red kite" }, TestContext.Current.CancellationToken)
            .AsTask();

        await Should.ThrowAsync<ProviderInvocationException>(() => clock.AdvanceUntilAsync(call));
    }

    private static ServiceProvider Build(StubHttpServer server, ManualClock clock, Action<TraconImageProviderOptions>? images = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTracon()
            .UseAzureOpenAI(options =>
            {
                options.ApiKey = TestData.ApiKey;
                options.Endpoint = server.BaseAddress;
            })
            .UseAzureOpenAIImages(provider =>
            {
                provider.Model = "image-deployment";
                images?.Invoke(provider);
            });

        return services.BuildServiceProvider();
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);
}
