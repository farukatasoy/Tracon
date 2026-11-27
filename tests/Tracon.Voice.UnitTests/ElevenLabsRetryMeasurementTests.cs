using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A synthesis that was sent twice because the first try failed is one measured call: one item in the step's record, with the
/// quantity of the answer that succeeded.
/// </summary>
/// <remarks>
/// The repeat lives inside the client, below the service that measures and records a call, so the record cannot see the failed try.
/// The case composes the service the way a host does (the container, a step, the service and the built-in client over a fake
/// handler) and reads what the run store holds, not what the service reports: a repeat that surfaced as a second call would write
/// two items and count the failed try's characters against the run's cost and quota.
/// </remarks>
public sealed class ElevenLabsRetryMeasurementTests
{
    private const string Provider = "elevenlabs-batch";

    [Fact]
    public async Task Two_tries_of_one_synthesis_are_one_item_with_the_quantity_of_the_try_that_succeeded()
    {
        var clock = new ManualClock();
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            var attempt = Interlocked.Increment(ref calls);
            var response = attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") }
                : StubHttpMessageHandler.Binary(DialogueTestData.Mp3Bytes);

            response.Headers.TryAddWithoutValidation("character-cost", attempt == 1 ? "99" : "11");

            return response;
        });

        using var client = new ElevenLabsSpeechClient(
            new VoiceOptions { ApiKey = "FAKE-KEY", DefaultVoiceId = "voice-1", MaxRetries = 1 },
            new HttpClient(handler),
            timeProvider: clock);

        var services = new ServiceCollection();

        services.AddTracon().AddSpeechSynthesizer(Provider, _ => client);

        await using var provider = services.BuildServiceProvider();

        var runId = Guid.CreateVersion7();
        var step = provider.GetRequiredService<ITraconStepRunner>().RunAsync(
            new TraconStepOptions { Name = "podcast-render", RunId = runId },
            async token => await provider.GetRequiredService<ISpeechSynthesisService>()
                .SynthesizeAsync(new SpeechRequest { Text = "hello" }, Provider, token).ConfigureAwait(false),
            TestContext.Current.CancellationToken).AsTask();

        await clock.AdvanceUntilAsync(step);

        var result = await step;

        handler.Requests.Count.ShouldBe(2, "the first try failed and the second one is the call");
        result.UsageRecorded.ShouldBeTrue();
        result.Usage.Quantity.ShouldBe(11m, "the failed try's header is not counted");
        result.Audio.CharactersSent.ShouldBe(5);

        var items = await provider.GetRequiredService<IRunStore>().ListNonTokenUsageAsync(runId, TestContext.Current.CancellationToken);

        items.ShouldHaveSingleItem().Usage.Quantity.ShouldBe(11m);
    }
}
