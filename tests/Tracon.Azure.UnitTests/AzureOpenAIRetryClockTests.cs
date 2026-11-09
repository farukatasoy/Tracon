using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// Every way this package builds an Azure OpenAI client hands it the clock the host registered, so the wait between two
/// tries of a failed call runs on that clock and not on the system's.
/// </summary>
/// <remarks>
/// <para>
/// The calls fail on a refused connection, which the client library retries three times with its ordinary backoff
/// (0.8, 1.6 and 3.2 seconds). The clock is a <see cref="ManualClock"/> and the call is carried to its end by stepping
/// it, so the test waits for none of them and measures none of them. A client built without the clock would wait the
/// backoff for real: nobody moves the clock, the steps stay empty, and the assertion fails.
/// </para>
/// <para>
/// What the wait does with a header, a cancellation or a ceiling is the shared policy tests' job; this class holds
/// only the wiring from the registration to the policy.
/// </para>
/// </remarks>
public sealed class AzureOpenAIRetryClockTests
{
    private static readonly TimeSpan[] LibraryBackoff = [TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(3.2)];

    [Fact]
    public async Task A_factory_built_for_the_setup_key_waits_on_the_clock()
    {
        var clock = new ManualClock();
        using var chat = new AzureOpenAIChatClientFactory(TestData.Options(o => o.Endpoint = ClosedEndpoint()), timeProvider: clock)
            .CreateChatClient(TestData.Binding());

        await AssertWaitsOnAsync(clock, chat);
    }

    [Fact]
    public async Task A_provider_hands_its_clock_to_the_client_it_builds_for_a_tenant_credential()
    {
        var clock = new ManualClock();
        var options = TestData.Options();
        var provider = new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(options),
            [],
            healthCheckOptions: options,
            timeProvider: clock);

        using var chat = provider.CreateChatClient(
            TestData.Binding(),
            new ModelProviderCredential { ApiKey = "tenant-key", Endpoint = ClosedEndpoint().ToString() });

        await AssertWaitsOnAsync(clock, chat);
    }

    [Fact]
    public async Task UseAzureOpenAI_builds_its_client_on_the_clock_the_host_registered()
    {
        var clock = new ManualClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddTracon().UseAzureOpenAI(options =>
        {
            options.ApiKey = TestData.ApiKey;
            options.Endpoint = ClosedEndpoint();
        });

        await using var provider = services.BuildServiceProvider();
        using var chat = provider.GetRequiredService<IModelProviderRegistry>().CreateChatClient(TestData.Binding());

        await AssertWaitsOnAsync(clock, chat);
    }

    private static async Task AssertWaitsOnAsync(ManualClock clock, IChatClient chat)
    {
        var call = chat.GetResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        await Should.ThrowAsync<Exception>(() => clock.AdvanceUntilAsync(call));

        clock.Steps.ShouldBe(LibraryBackoff, "the three retries of a refused connection each waited on the clock the client was given");
    }

    /// <summary>An address on this machine that nothing listens on, so a connection to it is refused at once.</summary>
    private static Uri ClosedEndpoint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return new Uri($"http://127.0.0.1:{port}/");
    }
}
