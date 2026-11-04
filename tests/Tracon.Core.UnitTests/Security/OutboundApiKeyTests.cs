using System.Net.Http.Headers;

namespace Tracon.Core.UnitTests.Security;

/// <summary>
/// Tests for <see cref="OutboundApiKey"/>: the one trim and check every key set up at start passes before a header is
/// built from it.
/// </summary>
/// <remarks>
/// The rule exists because an SDK that validates the header it builds quotes the whole value when it refuses it, and a
/// key set up at start has no per-call credential to mask in that text. So the tests that matter most are the ones that
/// read the failure: it names the option and never repeats the key.
/// </remarks>
public sealed class OutboundApiKeyTests
{
    private const string Option = "SampleProviderOptions.ApiKey";
    private const string Key = "test-key-0123456789abcdef";

    public static TheoryData<string> WhiteSpaceAroundTheKey => new()
    {
        { Key + "\n" },
        { Key + "\r\n" },
        { Key + "\r" },
        { Key + "\t" },
        { Key + " " },
        { "\n" + Key },
        { "\r\n" + Key },
        { "\t" + Key },
        { "  " + Key },
        { " \t\r\n" + Key + "\n\r\t " },
        { "\u00A0" + Key + "\u2028" },
    };

    public static TheoryData<string> ControlCharactersInsideTheKey => new()
    {
        { "test-key-\0-0123456789" },
        { "test-key-\n-0123456789" },
        { "test-key-\r-0123456789" },
        { "test-key-\r\nX-Injected: 1-0123456789" },
        { "test-key-\t-0123456789" },
        { "test-key-\u001b-0123456789" },
        { "test-key-\u007f-0123456789" },
        { "test-key-\u0085-0123456789" },
        { "test-key-\u009f-0123456789" },
        { "\0test-key-0123456789" },
        { "test-key-0123456789\0" },
    };

    public static TheoryData<string> CharactersOutsidePrintableAscii => new()
    {
        { "test-key-\u00E9-0123456789" },
        { "test-key-\u0100-0123456789" },
        { "test-key-\u200B-0123456789" },
        { "test-key-\uD83D\uDE00-0123456789" },
        { "test-key-\ud800-0123456789" },
        { "\u201Ctest-key-0123456789\u201D" },
    };

    public static TheoryData<string> KeysTheAuthorizationSyntaxRefuses => new()
    {
        { "test-key-\"-0123456789" },
        { "test-key-,-0123456789" },
        { "test,key" },
        { "\"test-key" },
    };

    public static TheoryData<string> KeysAHeaderCarries => new()
    {
        { Key },
        { "A" },
        { "abc_DEF-123.456~789+/==" },
        { "AIza0123456789-abcdefghijklmnopqrstuvwxyz_" },
        { "two words" },
        { "a=b=c" },
        { "!#$%&'*+-.^_`|~" },
        { "{[(<>)]}:;?@\\" },
        { "a=\"b\"" },
    };

    [Theory]
    [MemberData(nameof(WhiteSpaceAroundTheKey))]
    public void White_space_at_either_end_is_removed(string configured)
    {
        OutboundApiKey.Normalize(Option, configured).ShouldBe(Key);
        OutboundApiKey.Refusal(Option, configured).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(KeysAHeaderCarries))]
    public void A_key_a_header_carries_is_returned_as_it_is(string configured)
    {
        OutboundApiKey.Normalize(Option, configured).ShouldBe(configured);
        OutboundApiKey.Refusal(Option, configured).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData(" \t\r\n ")]
    [InlineData("\u00A0")]
    public void A_key_with_nothing_left_after_the_trim_is_reported_as_absent_and_not_as_refused(string? configured)
    {
        OutboundApiKey.Normalize(Option, configured).ShouldBeNull();
        OutboundApiKey.Refusal(Option, configured).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(ControlCharactersInsideTheKey))]
    public void A_control_character_inside_the_key_is_refused_and_the_message_names_the_option_and_not_the_key(string configured)
    {
        var failure = Should.Throw<TraconException>(() => OutboundApiKey.Normalize(Option, configured));

        failure.Message.ShouldStartWith(Option);
        failure.Message.ShouldContain("control character");
        ShouldNotRepeat(failure.Message, configured);
        OutboundApiKey.Refusal(Option, configured).ShouldBe(failure.Message);
    }

    [Theory]
    [MemberData(nameof(CharactersOutsidePrintableAscii))]
    public void A_character_outside_printable_ascii_is_refused_and_the_message_does_not_repeat_the_key(string configured)
    {
        var failure = Should.Throw<TraconException>(() => OutboundApiKey.Normalize(Option, configured));

        failure.Message.ShouldStartWith(Option);
        failure.Message.ShouldContain("outside printable ASCII");
        ShouldNotRepeat(failure.Message, configured);
        OutboundApiKey.Refusal(Option, configured).ShouldBe(failure.Message);
    }

    [Theory]
    [MemberData(nameof(KeysTheAuthorizationSyntaxRefuses))]
    public void A_quote_or_comma_the_authorization_syntax_refuses_is_refused_and_the_message_does_not_repeat_the_key(string configured)
    {
        var failure = Should.Throw<TraconException>(() => OutboundApiKey.Normalize(Option, configured));

        failure.Message.ShouldStartWith(Option);
        failure.Message.ShouldContain("Authorization");
        ShouldNotRepeat(failure.Message, configured);
        OutboundApiKey.Refusal(Option, configured).ShouldBe(failure.Message);
    }

    [Fact]
    public void A_very_long_key_is_returned_whole_and_a_very_long_refused_key_does_not_make_a_long_message()
    {
        var accepted = new string('k', 200_000);
        var refused = accepted + "\0" + accepted;

        OutboundApiKey.Normalize(Option, "\n" + accepted + "\n").ShouldBe(accepted);

        var failure = Should.Throw<TraconException>(() => OutboundApiKey.Normalize(Option, refused));

        failure.Message.Length.ShouldBeLessThan(1_000);
        failure.Message.ShouldNotContain("kkkkkkkk", Case.Sensitive);
    }

    [Fact]
    public void The_text_of_no_refusal_contains_any_part_of_the_key_it_refused()
    {
        // A key made of distinct, searchable pieces: if any piece is quoted, the search finds it.
        var pieces = new[] { "alpha-0f3a", "bravo-91c2", "charlie-77d4", "delta-5be8" };

        foreach (var offender in new[] { "\n", "\0", "\u00E9", "\"", "," })
        {
            var configured = pieces[0] + pieces[1] + offender + pieces[2] + pieces[3];
            var message = OutboundApiKey.Refusal(Option, configured);

            message.ShouldNotBeNull();

            foreach (var piece in pieces)
            {
                message.ShouldNotContain(piece, Case.Sensitive);
            }
        }
    }

    [Fact]
    public void Every_printable_ascii_character_is_either_accepted_by_the_header_syntax_or_refused()
    {
        // The rule claims to follow what the framework's own parser accepts. Ask the parser, character by character,
        // so a runtime that accepts more or less than today's breaks this test and not a deployment.
        for (var code = 0x21; code <= 0x7e; code++)
        {
            var character = (char)code;
            var configured = "abc" + character + "def";
            var refused = OutboundApiKey.Refusal(Option, configured) is not null;

            using var request = new HttpRequestMessage();
            var parserAccepts = TryAddHeaders(request, configured);

            parserAccepts.ShouldBe(!refused, $"character U+{code:X4}");
        }
    }

    [Fact]
    public void A_key_that_is_accepted_is_accepted_by_the_validating_authorization_and_custom_header_forms()
    {
        foreach (var configured in new[] { Key, "a=b=c", "!#$%&'*+-.^_`|~", "two words", "a=\"b\"", "abc_DEF-123.456~789+/==" })
        {
            var normalized = OutboundApiKey.Normalize(Option, configured);

            normalized.ShouldNotBeNull();

            using var request = new HttpRequestMessage();

            TryAddHeaders(request, normalized).ShouldBeTrue(configured);
        }
    }

    private static bool TryAddHeaders(HttpRequestMessage request, string key)
    {
        try
        {
            // The two forms the shipped senders use: the validated Authorization header the Anthropic client builds
            // from a string, and a custom key header.
            request.Headers.Add("Authorization", "Bearer " + key);
            request.Headers.Add("x-goog-api-key", key);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ShouldNotRepeat(string message, string configured)
    {
        message.ShouldNotContain(configured, Case.Sensitive);
        message.ShouldNotContain("test-key-", Case.Sensitive);
        message.ShouldNotContain("0123456789", Case.Sensitive);
    }
}
