using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;
using Tracon.Tests.Common;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The host's own image call (<see cref="IImageGenerationService"/>) over the registered OpenAI client and a real socket: what
/// the call's settings put on the wire, and how often a failing call reaches the server.
/// </summary>
/// <remarks>
/// <para>
/// The server answers every request and counts them, so a number here is what a provider would have billed for. The wait
/// between two tries runs on a <see cref="ManualClock"/> the host registered: nothing waits for a backoff.
/// </para>
/// <para>
/// What a ceiling does to a message is the shared policy tests' job; this class holds the wiring from a request and from the
/// options of the provider to the client.
/// </para>
/// </remarks>
public sealed class OpenAIImageServiceTests
{
    private const string Answer = """{"created":1,"data":[{"b64_json":"iVBORw0KGgoAAAAA"}]}""";

    [Fact]
    public async Task A_call_setting_is_the_quality_on_the_wire_for_that_call_and_the_registered_one_returns_on_the_next()
    {
        await using var server = StubHttpServer.RespondToEvery(200, "OK", Answer);
        await using var provider = Build(server, new ManualClock(), registered => registered.ProviderSettings["openai.quality"] = Text("medium"));
        var images = provider.GetRequiredService<IImageGenerationService>();

        await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a red kite", ProviderSettings = { ["openai.quality"] = Text("high") } },
            TestContext.Current.CancellationToken);
        await images.GenerateAsync(new TraconImageRequest { Prompt = "a red kite" }, TestContext.Current.CancellationToken);

        var bodies = server.RequestBodies;

        bodies.Count.ShouldBe(2);
        bodies[0].ShouldContain("\"quality\":\"high\"");
        bodies[0].ShouldNotContain("medium", Case.Sensitive);
        bodies[1].ShouldContain("\"quality\":\"medium\"");
        bodies[1].ShouldNotContain("high", Case.Sensitive);
    }

    [Fact]
    public async Task A_call_setting_the_provider_does_not_support_sends_no_request()
    {
        await using var server = StubHttpServer.RespondToEvery(200, "OK", Answer);
        await using var provider = Build(server, new ManualClock());
        var images = provider.GetRequiredService<IImageGenerationService>();

        await Should.ThrowAsync<TraconException>(
            async () => await images.GenerateAsync(
                new TraconImageRequest { Prompt = "a red kite", ProviderSettings = { ["openai.size"] = Text("large") } },
                TestContext.Current.CancellationToken));

        server.Requests.ShouldBe(0, "a refused setting is refused before anything leaves the process");
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
    public async Task A_ceiling_above_the_count_of_the_client_changes_nothing()
    {
        await using var server = StubHttpServer.RespondToEvery(503, "Service Unavailable", "{}");
        var clock = new ManualClock();
        await using var provider = Build(server, clock, registered => registered.MaxRetries = 6, openAI => openAI.MaxRetries = 1);

        await FailAsync(provider, clock);

        server.Requests.ShouldBe(2, "the count of the client, one retry, is the lower of the two");
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
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:Images:Providers:openai:MaxRetries"] = "0" })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTracon(configuration.GetSection("Tracon"))
            .UseOpenAI(options =>
            {
                options.ApiKey = TestData.ApiKey;
                options.Endpoint = new Uri(server.BaseAddress, "v1/");
            })
            .UseOpenAIImages(provider => provider.Model = "gpt-image-1");

        await using var host = services.BuildServiceProvider();

        await FailAsync(host, clock);

        server.Requests.ShouldBe(1, "the model was set in code and the ceiling in configuration: both reached the same entry");
    }

    private static async Task FailAsync(ServiceProvider provider, ManualClock clock)
    {
        var call = provider.GetRequiredService<IImageGenerationService>()
            .GenerateAsync(new TraconImageRequest { Prompt = "a red kite" }, TestContext.Current.CancellationToken)
            .AsTask();

        await Should.ThrowAsync<ProviderInvocationException>(() => clock.AdvanceUntilAsync(call));
    }

    private static ServiceProvider Build(
        StubHttpServer server,
        ManualClock clock,
        Action<TraconImageProviderOptions>? images = null,
        Action<OpenAIProviderOptions>? openAI = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTracon()
            .UseOpenAI(options =>
            {
                options.ApiKey = TestData.ApiKey;
                options.Endpoint = new Uri(server.BaseAddress, "v1/");
                openAI?.Invoke(options);
            })
            .UseOpenAIImages(provider =>
            {
                provider.Model = "gpt-image-1";
                images?.Invoke(provider);
            });

        return services.BuildServiceProvider();
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);
}
