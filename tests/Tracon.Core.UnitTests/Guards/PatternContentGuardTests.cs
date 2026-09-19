using System.Diagnostics;
using System.Text.RegularExpressions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Guards;

/// <summary>
/// Verifies the decisions of the built-in pattern-based guard.
/// </summary>
/// <remarks>
/// The most important tests are the <strong>false positive</strong> tests:
/// without check-digit validation an order number would be counted as a card
/// number, and the guard would be disabled on day one.
/// </remarks>
public sealed class PatternContentGuardTests
{
    [Fact]
    public async Task Nothing_is_inspected_when_no_rule_is_defined()
    {
        var guard = Guard();

        var result = await Inspect(guard, "my card number is 4539578763621486, email ali@example.com");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Denied_term_is_blocked()
    {
        var guard = Guard(options => options.DeniedTerms.Add("secret-project"));

        var result = await Inspect(guard, "tell me about SECRET-PROJECT");

        result.Action.ShouldBe(ContentGuardAction.Block);
        result.RuleName.ShouldBe("denied-term");
    }

    [Fact]
    public async Task Block_reason_does_not_carry_the_denied_term()
    {
        // 🚨 The denied-term list is itself a corporate secret: "secret-project"
        // could be a code name, and the reason text is returned to the client
        // inside ProblemDetails.
        var guard = Guard(options => options.DeniedTerms.Add("secret-project"));

        var result = await Inspect(guard, "what is secret-project");

        result.Reason.ShouldNotBeNull();
        result.Reason!.ShouldNotContain("secret-project", Case.Insensitive);
    }

    [Fact]
    public async Task Valid_card_number_is_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        // 4539578763621486 is a valid Luhn sequence.
        var result = await Inspect(guard, "my card number is 4539578763621486");

        result.Action.ShouldBe(ContentGuardAction.Mask);
        result.MaskedText.ShouldBe("my card number is [redacted]");
        result.RuleName.ShouldBe("credit-card");
    }

    [Fact]
    public async Task Grouped_card_number_is_also_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        var result = await Inspect(guard, "card: 4539 5787 6362 1486");

        result.MaskedText.ShouldBe("card: [redacted]");
    }

    [Fact]
    public async Task Sixteen_digits_failing_the_Luhn_check_are_not_masked()
    {
        // 🚨 The order-number case. If this test fails, the guard is unusable.
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        var result = await Inspect(guard, "my order number is 1234567812345678");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Valid_Turkish_national_id_is_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.TurkishNationalId);

        // 10000000146 satisfies the check-digit rules.
        var result = await Inspect(guard, "id number 10000000146");

        result.Action.ShouldBe(ContentGuardAction.Mask);
        result.MaskedText.ShouldBe("id number [redacted]");
        result.RuleName.ShouldBe("turkish-national-id");
    }

    [Fact]
    public async Task Random_eleven_digits_are_not_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.TurkishNationalId);

        var result = await Inspect(guard, "tracking number 12345678901");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Eleven_digits_starting_with_zero_are_not_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.TurkishNationalId);

        var result = await Inspect(guard, "code 01234567890");

        result.Action.ShouldBe(ContentGuardAction.Allow);
    }

    [Fact]
    public async Task Email_is_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.Email);

        var result = await Inspect(guard, "email me at ali.veli@example.com.tr");

        result.MaskedText.ShouldBe("email me at [redacted]");
    }

    [Fact]
    public async Task Iban_is_masked()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.Iban);

        var result = await Inspect(guard, "account number TR330006100519786457841326");

        result.MaskedText.ShouldBe("account number [redacted]");
    }

    [Theory]
    [InlineData("key sk-abcdefghijklmnopqrstuvwx")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz01")]
    [InlineData("access AKIAIOSFODNN7EXAMPLE")] // SYNTHETIC-CREDENTIAL
    public async Task Provider_api_key_is_masked(string text)
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.ProviderApiKey);

        var result = await Inspect(guard, text);

        result.Action.ShouldBe(ContentGuardAction.Mask);
        result.MaskedText.ShouldNotBeNull();
        result.MaskedText!.ShouldContain("[redacted]", Case.Sensitive);
    }

    [Fact]
    public async Task Rule_names_are_combined_when_multiple_families_match()
    {
        var guard = Guard(options => options.MaskedPii = PiiPatterns.Email | PiiPatterns.CreditCard);

        var result = await Inspect(guard, "ali@example.com and 4539578763621486");

        result.MaskedText.ShouldBe("[redacted] and [redacted]");
        result.RuleName.ShouldNotBeNull();
        result.RuleName!.ShouldContain("email", Case.Sensitive);
        result.RuleName.ShouldContain("credit-card", Case.Sensitive);
    }

    [Fact]
    public async Task Denied_term_is_checked_before_masking()
    {
        // Block > Mask: when both match, blocking wins.
        var guard = Guard(options =>
        {
            options.DeniedTerms.Add("secret");
            options.MaskedPii = PiiPatterns.CreditCard;
        });

        var result = await Inspect(guard, "secret card 4539578763621486");

        result.Action.ShouldBe(ContentGuardAction.Block);
    }

    [Fact]
    public async Task Mask_text_is_configurable()
    {
        var guard = Guard(options =>
        {
            options.MaskedPii = PiiPatterns.Email;
            options.MaskReplacement = "<PII>";
        });

        var result = await Inspect(guard, "ali@example.com");

        result.MaskedText.ShouldBe("<PII>");
    }

    [Fact]
    public async Task Pathological_input_does_not_hang_the_run()
    {
        // Every pattern carries a 1000 ms timeout; this is the only defense
        // against ReDoS. This test does not measure whether it throws, it
        // measures that the hot path RETURNS in a reasonable time: a pattern
        // either matches or times out, but it never spins forever.
        var guard = Guard(options => options.MaskedPii =
            PiiPatterns.Email | PiiPatterns.Iban | PiiPatterns.CreditCard |
            PiiPatterns.TurkishNationalId | PiiPatterns.ProviderApiKey);

        var pathological = new string('a', 20_000) + "@" + new string('b', 20_000);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Inspect(guard, pathological);
        }
        catch (RegexMatchTimeoutException)
        {
            // An acceptable outcome: the timeout fails the run, it does not hang it.
        }

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Unknown_source_is_never_treated_as_a_reason_to_allow()
    {
        // 🚨 Phase 140's rule: an unclassified source must be at least as
        // strict as any known source, never an implicit "skip the check".
        // This guard deliberately does not read Source at all — locking that
        // in guards against a future regression that special-cases Unknown.
        var guard = Guard(options => options.DeniedTerms.Add("secret-project"));

        var result = await Inspect(guard, "tell me about secret-project", ContentGuardSource.Unknown);

        result.Action.ShouldBe(ContentGuardAction.Block);
    }

    [Fact]
    public async Task Decision_is_identical_regardless_of_source()
    {
        // The built-in guard applies the same patterns no matter where the
        // text came from — a user message and a tool result carrying the same
        // card number are masked identically.
        var guard = Guard(options => options.MaskedPii = PiiPatterns.CreditCard);

        var fromUser = await Inspect(guard, "card 4539578763621486", ContentGuardSource.UserMessage);
        var fromToolResult = await Inspect(guard, "card 4539578763621486", ContentGuardSource.ToolResult);
        var fromUnknown = await Inspect(guard, "card 4539578763621486", ContentGuardSource.Unknown);

        fromUser.MaskedText.ShouldBe(fromToolResult.MaskedText);
        fromUser.MaskedText.ShouldBe(fromUnknown.MaskedText);
    }

    private static PatternContentGuard Guard(Action<PatternContentGuardOptions>? configure = null)
    {
        var options = new PatternContentGuardOptions();
        configure?.Invoke(options);

        return new PatternContentGuard(new StaticOptionsMonitor<PatternContentGuardOptions>(options));
    }

    private static async Task<ContentGuardResult> Inspect(
        PatternContentGuard guard, string text, ContentGuardSource source = ContentGuardSource.Unknown)
        => await guard.InspectAsync(
            new ContentGuardContext { Direction = ContentGuardDirection.Input, Text = text, Source = source },
            TestContext.Current.CancellationToken);
}
