using System.Net;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A dialogue takes one slot of the client's single concurrency limit, gives it back on
/// every exit, and does not reach the network when it was cancelled first.
/// </summary>
/// <remarks>
/// The limit is one counter for synthesis, dialogue and transcription (the same
/// instance serves all three). A dialogue that took two slots would halve the limit; one
/// that kept its slot after a cancellation or a provider error would lock the client.
/// </remarks>
public sealed class ElevenLabsDialogueConcurrencyTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_cancelled_dialogue_never_reaches_the_network()
    {
        var handler = new GatedHandler();
        using var client = Create(handler, maxConcurrent: 1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), cancellation.Token));

        handler.Entered.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelling_a_dialogue_in_flight_reaches_the_send_and_returns_the_slot()
    {
        var handler = new GatedHandler();
        using var client = Create(handler, maxConcurrent: 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var dialogue = client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), cancellation.Token).AsTask();

        await handler.WaitForEntriesAsync(1);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(dialogue);
        handler.ObservedCancellation.ShouldBeTrue("the token must reach the provider call");

        // With a limit of one, a leaked slot would hold this call forever.
        handler.Release();
        var next = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        next.Audio.Data.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_provider_error_returns_the_slot()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler(_ => Interlocked.Increment(ref calls) == 1
            ? DialogueTestData.Failure(HttpStatusCode.InternalServerError, "boom")
            : DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.MaxConcurrentRequests = 1);

        await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken)
                .AsTask()
                .WaitAsync(Patience, TestContext.Current.CancellationToken));

        var next = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        next.Audio.Data.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_dialogue_takes_one_slot_of_the_shared_limit()
    {
        // Limit two: a dialogue in flight leaves room for exactly one single-voice call.
        var handler = new GatedHandler();
        using var client = Create(handler, maxConcurrent: 2);

        var dialogue = client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken).AsTask();
        await handler.WaitForEntriesAsync(1);

        var speech = client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken).AsTask();
        await handler.WaitForEntriesAsync(2);

        handler.Release();
        await Task.WhenAll(dialogue, speech).WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_dialogue_and_a_single_voice_call_share_one_counter()
    {
        // Limit one: the single-voice call waits for the dialogue's slot. Two counters
        // would let both reach the provider at once.
        var handler = new GatedHandler();
        using var client = Create(handler, maxConcurrent: 1);

        var dialogue = client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken).AsTask();
        await handler.WaitForEntriesAsync(1);

        var speech = client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        handler.Entered.ShouldBe(1, "the single-voice call reached the provider while the dialogue held the only slot");

        handler.Release();
        await Task.WhenAll(dialogue, speech).WaitAsync(Patience, TestContext.Current.CancellationToken);
        handler.Entered.ShouldBe(2);
    }

    private static ElevenLabsSpeechClient Create(HttpMessageHandler handler, int maxConcurrent)
        => new(
            new VoiceOptions { ApiKey = "FAKE-KEY", DefaultVoiceId = "voice-1", MaxConcurrentRequests = maxConcurrent },
            new HttpClient(handler));

    /// <summary>A handler that holds every request until the test releases it, and watches the token.</summary>
    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _entries = new(0);
        private int _entered;
        private int _observedCancellation;

        public int Entered => Volatile.Read(ref _entered);

        public bool ObservedCancellation => Volatile.Read(ref _observedCancellation) == 1;

        public void Release() => _gate.TrySetResult();

        public async Task WaitForEntriesAsync(int count)
        {
            while (Entered < count)
            {
                (await _entries.WaitAsync(Patience)).ShouldBeTrue($"only {Entered} of {count} requests reached the provider");
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _entered);
            _entries.Release();

            try
            {
                await _gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _observedCancellation, 1);
                throw;
            }

            return DialogueTestData.RawResponse();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _entries.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
