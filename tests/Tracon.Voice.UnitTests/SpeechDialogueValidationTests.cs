using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A dialogue over a limit, or with a turn that has no text or no voice, is refused
/// before the network: the provider receives nothing.
/// </summary>
/// <remarks>
/// Splitting is the caller's job: the client never cuts a dialogue to fit. The
/// message names the limit and the measured count, never a turn's text.
/// </remarks>
public sealed class SpeechDialogueValidationTests
{
    private const string Secretive = "do-not-echo-this-turn";

    [Theory]
    [InlineData("no turn", "at least one turn")]
    [InlineData("null turns", "at least one turn")]
    [InlineData("null turn", "turn 1 is missing")]
    [InlineData("blank text", "turn 1 has no text")]
    [InlineData("blank voice", "turn 0 has no voice id")]
    [InlineData("eleven voices", "uses 11 voices")]
    public async Task A_dialogue_that_breaks_a_rule_is_refused_before_the_network(string because, string expected)
    {
        var request = because switch
        {
            "no turn" => new SpeechDialogueRequest { Turns = [] },
            "null turns" => new SpeechDialogueRequest { Turns = null! },
            "null turn" => new SpeechDialogueRequest { Turns = [Turn("a"), null!] },
            "blank text" => new SpeechDialogueRequest { Turns = [Turn("a"), new SpeechDialogueTurn { Text = "  ", VoiceId = "v" }] },
            "blank voice" => new SpeechDialogueRequest { Turns = [new SpeechDialogueTurn { Text = Secretive, VoiceId = " " }] },
            _ => new SpeechDialogueRequest { Turns = [.. Enumerable.Range(0, 11).Select(static index => Turn($"v{index}"))] },
        };

        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(request, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(expected, Case.Insensitive, because);
        exception.Message.ShouldNotContain(Secretive);
        handler.Requests.ShouldBeEmpty(because);
    }

    [Fact]
    public async Task A_dialogue_over_the_character_limit_names_the_limit_and_the_count_and_reaches_nothing()
    {
        // Twelve turns, the shape of manual case 5: 12 x 170 = 2,040 characters over a 2,000 limit.
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        var turns = Enumerable.Range(0, 12)
            .Select(index => new SpeechDialogueTurn { Text = Secretive.PadRight(170, 'x'), VoiceId = index % 2 == 0 ? "voice-a" : "voice-b" })
            .ToList();

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(
                new SpeechDialogueRequest { Turns = turns },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("2040 characters");
        exception.Message.ShouldContain("at most 2000");
        exception.Message.ShouldContain(nameof(VoiceOptions.MaxDialogueCharactersPerRequest));
        exception.Message.ShouldNotContain(Secretive);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dialogue_at_the_limits_is_sent()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.MaxDialogueCharactersPerRequest = 100);

        // Ten distinct voices, exactly 100 characters, and a voice used twice counts once.
        var turns = Enumerable.Range(0, 10)
            .Select(static index => new SpeechDialogueTurn { Text = new string('a', 9), VoiceId = $"voice-{index}" })
            .Append(new SpeechDialogueTurn { Text = new string('b', 10), VoiceId = "voice-0" })
            .ToList();

        _ = await client.SynthesizeDialogueAsync(new SpeechDialogueRequest { Turns = turns }, TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_configured_limit_is_the_one_applied()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.MaxDialogueCharactersPerRequest = 10);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("14 characters");
        exception.Message.ShouldContain("at most 10");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_null_request_is_an_argument_error()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await client.SynthesizeDialogueAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_tenant_credential_is_refused_and_never_sent_on_the_configured_key()
    {
        // The built-in client takes no tenant credential. The resolver never sends
        // one; a caller that calls the client directly with one must not be served on
        // the host's key.
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        await Should.ThrowAsync<NotSupportedException>(
            async () => await client.SynthesizeDialogueAsync(
                DialogueTestData.TwoTurns() with { Credential = new ModelProviderCredential { ApiKey = "tenant-key-value" } },
                TestContext.Current.CancellationToken));

        handler.Requests.ShouldBeEmpty();
    }

    private static SpeechDialogueTurn Turn(string voiceId) => new() { Text = "line", VoiceId = voiceId };
}
