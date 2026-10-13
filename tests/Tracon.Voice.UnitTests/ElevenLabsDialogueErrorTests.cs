using System.Net;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A provider error on a dialogue names the status, never the key, the address, the
/// turns' text or the provider's body.
/// </summary>
/// <remarks>
/// The provider's error body echoes the request (the spoken text) and can carry a key
/// fragment; the message reaches logs and run records.
/// </remarks>
public sealed class ElevenLabsDialogueErrorTests
{
    private const string Key = "FAKE-KEY-DIALOGUE-SECRET";
    private const string Host = "voice-proxy.internal.example";
    private const string TurnText = "the-turn-text-must-not-leak";

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "HTTP 400")]
    [InlineData(HttpStatusCode.Unauthorized, "The API key is invalid")]
    [InlineData(HttpStatusCode.NotFound, "voice or model id was not found")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "rejected by the provider")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    public async Task A_provider_error_names_the_status_and_nothing_secret(HttpStatusCode status, string expected)
    {
        var handler = new StubHttpMessageHandler(
            _ => DialogueTestData.Failure(status, $"{{\"detail\":\"{TurnText} {Key} https://{Host}/\"}}"));
        using var client = DialogueTestData.CreateClient(handler, static options =>
        {
            options.ApiKey = Key;
            options.Endpoint = new Uri($"https://{Host}/");
        });

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(
                new SpeechDialogueRequest
                {
                    Turns =
                    [
                        new SpeechDialogueTurn { Text = TurnText, VoiceId = "voice-a" },
                        new SpeechDialogueTurn { Text = "second", VoiceId = "voice-b" },
                    ],
                    IncludeTimestamps = status == HttpStatusCode.BadRequest,
                },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldStartWith("Dialogue could not be generated");
        exception.Message.ShouldContain(expected);
        exception.Message.ShouldNotContain(Key);
        exception.Message.ShouldNotContain(Host);
        exception.Message.ShouldNotContain(TurnText);

        // The request went to the configured address with the configured key.
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.Host.ShouldBe(Host);
        request.Headers["xi-api-key"].ShouldBe(Key);
    }
}
