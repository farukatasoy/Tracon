using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A live voice session is created and then attached, and both calls carry the key that was read before the session was
/// created, even when the key rotates between them.
/// </summary>
/// <remarks>
/// Measured against <see cref="FakeGptLiveServer"/>, a real loopback server that records the key of the create call and of
/// the attach handshake. The launcher reads the pair once; this crosses the launcher, the provider, the session host and
/// the socket, which a unit test of the provider alone does not.
/// </remarks>
public sealed class LiveSetupVoiceTests
{
    private const string Agent = "code-agent";
    private const string KeyName = "Platform:OpenAI:Key";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n";

    [Fact]
    public async Task A_rotation_between_create_and_attach_never_splits_a_session_across_two_keys()
    {
        await using var server = await FakeGptLiveServer.StartAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "key-one-0001")]).Build();

        // The key rotates after the service has read the create call and before the attach handshake.
        server.AfterCreateRead = () => configuration[KeyName] = "key-two-0002";

        await using var host = await StartAsync(server, configuration);

        using (var first = await CreateAsync(host))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        await server.WaitForAttachAsync(LiveVoiceTests.Patience);

        server.CreateAuthorizationHeaders.ShouldBe(["Bearer key-one-0001"]);
        server.AttachAuthorizationHeaders.ShouldAllBe(static header => string.Equals(header, "Bearer key-one-0001", StringComparison.Ordinal));
        server.AttachAuthorizationHeaders.ShouldNotBeEmpty();

        // The next session reads the pair again.
        using (var second = await CreateAsync(host))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        server.CreateAuthorizationHeaders.ShouldBe(["Bearer key-one-0001", "Bearer key-two-0002"]);
    }

    private static Task<TraconTestHost> StartAsync(FakeGptLiveServer server, IConfiguration configuration)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseOpenAI(options =>
                {
                    options.Endpoint = server.BaseAddress;
                    options.ApiKeyConfigurationName = KeyName;
                })
                .UseOpenAILive(options => options.Endpoint = server.BaseAddress)
                .UseLiveVoice(static options => options.PersistTranscript = true),
            configureServices: services =>
            {
                services.AddSingleton(configuration);

                // The fake server is on the loopback interface; the live address always goes through the guard.
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
            });

    private static Task<HttpResponseMessage> CreateAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = Guid.NewGuid().ToString("N"), Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);
}
