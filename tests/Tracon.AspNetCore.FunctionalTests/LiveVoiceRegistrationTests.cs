using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the live voice surface answers when it is only half configured.
/// </summary>
/// <remarks>
/// <para>
/// The two answers are deliberately different. Without <c>UseLiveVoice()</c> the
/// route <strong>does not exist</strong> and the address is a <c>404</c>: a
/// capability that changes the hosting model must not appear as "present but off".
/// With the layer on but no provider registered the address exists and answers
/// <c>501</c>, naming the call that is missing.
/// </para>
/// <para>
/// 🚨 The last test here is the optional-dependency trap, and it needs a REAL
/// host. The built-in container treats an unregistered constructor parameter as
/// required even when the parameter has a C# default value, so the launcher resolves
/// its optional dependencies with <c>GetService</c> inside a factory. A constructor
/// injection would throw while the container was built — which no unit test that
/// news up the class by hand would ever see.
/// </para>
/// </remarks>
public sealed class LiveVoiceRegistrationTests
{
    private const string Agent = "code-agent";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n";

    [Fact]
    public async Task Without_UseLiveVoice_the_route_does_not_exist()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddAgent(TestData.Definition(Agent)));

        using var response = await PostAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task With_UseLiveVoice_but_no_provider_the_answer_is_501()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseLiveVoice());

        using var response = await PostAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The message names the call that is missing, so the operator can act on it.
        body.ShouldContain("UseOpenAILive", Case.Sensitive);
    }

    [Fact]
    public async Task The_layer_builds_without_a_provider_registered()
    {
        // 🚨 The optional-dependency trap. Resolving the launcher is what would
        // throw if ILiveVoiceProvider or ContentGuardPipeline were taken by plain
        // constructor injection instead of GetService inside a factory.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseLiveVoice());

        var launcher = host.Services.GetRequiredService<LiveVoiceSessionLauncher>();

        launcher.IsReady.ShouldBeFalse();
        host.Services.GetService<ILiveVoiceProvider>().ShouldBeNull();
    }

    [Fact]
    public async Task The_live_layer_is_independent_of_the_conversation_layer()
    {
        // The two voice designs are separate capabilities and must not require one
        // another: this host enables the live layer and NOT UseVoiceConversation().
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        host.Services.GetService<VoiceConversationDriver>().ShouldBeNull();
        host.Services.GetRequiredService<LiveVoiceSessionLauncher>().IsReady.ShouldBeTrue();

        using var response = await PostAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_consumers_own_provider_wins_over_the_built_in_one()
    {
        // TryAdd, both ways round: a consumer that registered their own live provider
        // before the call keeps it.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseOpenAI(options => options.ApiKey = "test-openai-key")
                .UseOpenAILive()
                .UseLiveVoice(),
            configureServices: static services =>
                services.AddSingleton<ILiveVoiceProvider>(new StubLiveVoiceProvider()));

        host.Services.GetRequiredService<ILiveVoiceProvider>()
            .ShouldBeOfType<StubLiveVoiceProvider>();
    }

    private static Task<HttpResponseMessage> PostAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "session-1", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

    private sealed class StubLiveVoiceProvider : ILiveVoiceProvider
    {
        public string ProviderName => "stub";

        public string ModelId => "stub-live";

        public int MaxAppendCharacters => 1000;

        public ValueTask<LiveVoiceSessionHandle> CreateSessionAsync(
            LiveVoiceCreateRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new LiveVoiceSessionHandle
            {
                ProviderSessionId = "stub-session",
                SdpAnswer = "v=0\r\n",
            });

        public ValueTask<ILiveVoiceSideband> AttachAsync(
            string providerSessionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The stub provider does not attach.");
    }
}
