using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The voice key set up at start is trimmed before it becomes the <c>xi-api-key</c> header, and a key that still holds a
/// character an HTTP header cannot carry is refused at start and again by the client, without repeating it.
/// </summary>
/// <remarks>
/// The client adds this header without validation, so a line break in the key would reach the wire as it is; that is
/// why the client and the options validator both go through the shared outbound key rule.
/// </remarks>
public sealed class ElevenLabsApiKeyTests
{
    private const string Key = "test-voice-key-0123456789";
    private const string OptionName = "VoiceOptions.ApiKey";

    private static readonly byte[] Mp3Bytes = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData(" ")]
    public async Task The_header_carries_the_key_without_the_white_space_around_it(string around)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler, around + Key + around);

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();

        request.Headers["xi-api-key"].ShouldBe(Key);
    }

    [Theory]
    [InlineData("test-voice-key-\0-0123456789")]
    [InlineData("test-voice-key-\n-0123456789")]
    [InlineData("test-voice-key-\u00E9-0123456789")]
    public async Task A_key_a_header_cannot_carry_stops_the_call_before_it_is_made_and_the_failure_does_not_repeat_it(string configured)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler, configured);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));

        failure.Message.ShouldStartWith(OptionName);
        failure.Message.ShouldNotContain("test-voice-key-", Case.Sensitive);
        failure.Message.ShouldNotContain("0123456789", Case.Sensitive);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void The_options_validator_accepts_a_key_that_needs_only_a_trim_and_refuses_one_a_header_cannot_carry()
    {
        var validator = new VoiceOptionsValidator(new VoiceProviderBinding(customSynthesizer: false, customTranscriber: false));

        validator.Validate(name: null, new VoiceOptions { ApiKey = Key + "\n" }).Succeeded.ShouldBeTrue();

        var refused = validator.Validate(name: null, new VoiceOptions { ApiKey = "test-voice-key-\0-0123456789" });

        refused.Failed.ShouldBeTrue();

        var text = string.Join('\n', refused.Failures!);

        text.ShouldContain(OptionName);
        text.ShouldNotContain("test-voice-key-", Case.Sensitive);
        text.ShouldNotContain("0123456789", Case.Sensitive);
    }

    private static ElevenLabsSpeechClient CreateClient(StubHttpMessageHandler handler, string apiKey)
        => new(new VoiceOptions { ApiKey = apiKey, DefaultVoiceId = "voice-1" }, new HttpClient(handler));
}
