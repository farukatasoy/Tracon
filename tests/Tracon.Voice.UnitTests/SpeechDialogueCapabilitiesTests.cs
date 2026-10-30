using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The dialogue rule every implementation applies before it calls its service: the built-in ElevenLabs client and a
/// third-party adapter refuse the same requests in the same words.
/// </summary>
/// <remarks>
/// The rule moved out of the ElevenLabs client so a second dialogue implementation does not copy it.
/// <c>SpeechDialogueValidationTests</c> keeps proving the ElevenLabs client refuses with it.
/// </remarks>
public sealed class SpeechDialogueCapabilitiesTests
{
    private const string Secretive = "do-not-echo-this-turn";

    private static readonly SpeechDialogueCapabilities Limits = new() { MaxVoices = 2, MaxCharactersPerRequest = 20 };

    [Fact]
    public void A_request_within_the_limits_returns_the_characters_of_all_turns()
    {
        var characters = Limits.Check(
            new SpeechDialogueRequest { Turns = [Turn("a", "12345"), Turn("b", "6789"), Turn("a", "0")] },
            "contoso");

        characters.ShouldBe(10);
    }

    [Fact]
    public void A_request_at_the_character_limit_is_accepted()
        => Limits.Check(new SpeechDialogueRequest { Turns = [Turn("a", new string('x', 20))] }, "contoso").ShouldBe(20);

    [Fact]
    public void A_voice_used_by_many_turns_counts_once_and_voice_ids_are_compared_exactly()
    {
        var request = new SpeechDialogueRequest { Turns = [Turn("a"), Turn("b"), Turn("a"), Turn("b")] };

        Limits.Check(request, "contoso").ShouldBe(16);

        // "A" and "a" are two voices: ids are the provider's, and the provider may tell them apart.
        Should.Throw<TraconException>(
                () => Limits.Check(new SpeechDialogueRequest { Turns = [Turn("a"), Turn("A"), Turn("b")] }, "contoso"))
            .Message.ShouldContain("uses 3 voices");
    }

    [Theory]
    [InlineData("no turn", "at least one turn")]
    [InlineData("null turns", "at least one turn")]
    [InlineData("null turn", "Dialogue turn 1 is missing.")]
    [InlineData("blank text", "Dialogue turn 1 has no text.")]
    [InlineData("blank voice", "Dialogue turn 0 has no voice id. A dialogue turn has no default voice.")]
    public void A_request_with_a_broken_turn_is_refused_without_the_text(string because, string expected)
    {
        var request = because switch
        {
            "no turn" => new SpeechDialogueRequest { Turns = [] },
            "null turns" => new SpeechDialogueRequest { Turns = null! },
            "null turn" => new SpeechDialogueRequest { Turns = [Turn("a"), null!] },
            "blank text" => new SpeechDialogueRequest { Turns = [Turn("a"), Turn("b", " ")] },
            _ => new SpeechDialogueRequest { Turns = [Turn(" ", Secretive)] },
        };

        var exception = Should.Throw<TraconException>(() => Limits.Check(request, "contoso"), because);

        exception.Message.ShouldContain(expected);
        exception.Message.ShouldNotContain(Secretive);
    }

    [Fact]
    public void Too_many_voices_names_the_provider_the_count_and_the_limit()
    {
        var exception = Should.Throw<TraconException>(
            () => Limits.Check(new SpeechDialogueRequest { Turns = [Turn("a"), Turn("b"), Turn("c")] }, "contoso"));

        exception.Message.ShouldBe(
            "The dialogue uses 3 voices; one 'contoso' dialogue request takes at most 2. Split it into requests of at most 2 voices.");
    }

    [Fact]
    public void Too_many_characters_names_the_provider_the_count_the_limit_and_the_setting_when_there_is_one()
    {
        var request = new SpeechDialogueRequest { Turns = [Turn("a", new string('x', 12)), Turn("b", new string('y', 12))] };

        Should.Throw<TraconException>(() => Limits.Check(request, "contoso", "ContosoOptions.MaxDialogue")).Message.ShouldBe(
            "The dialogue has 24 characters; one 'contoso' dialogue request takes at most 20 (ContosoOptions.MaxDialogue). " +
            "Split it into smaller requests: Tracon does not split or cut a dialogue.");

        Should.Throw<TraconException>(() => Limits.Check(request, "contoso")).Message.ShouldBe(
            "The dialogue has 24 characters; one 'contoso' dialogue request takes at most 20. " +
            "Split it into smaller requests: Tracon does not split or cut a dialogue.");
    }

    [Fact]
    public void A_null_request_and_a_blank_provider_name_are_argument_errors()
    {
        Should.Throw<ArgumentNullException>(() => Limits.Check(null!, "contoso"));
        Should.Throw<ArgumentException>(() => Limits.Check(new SpeechDialogueRequest { Turns = [Turn("a")] }, " "));
    }

    private static SpeechDialogueTurn Turn(string voiceId, string text = "line")
        => new() { Text = text, VoiceId = voiceId };
}
