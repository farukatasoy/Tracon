using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Feeds every scalar a configuration key can set the text an operator mistypes, under the cultures whose
/// number format differs from the invariant one, and holds the options pipeline to one answer: the value binds
/// to what the invariant reading says, or the host refuses to start and the message names the key.
/// </summary>
/// <remarks>
/// <para>
/// <c>UnreadableValueProbeTests</c> writes one text no type can read. This class writes the texts that CAN be
/// read and are wrong: a comma decimal, <c>-1</c>, <c>0</c>, an overflow, <c>NaN</c>, a time span with too many
/// parts. A text the reader takes and the validator lets through is the dangerous case: the setting looks
/// applied and means something else (a limit of zero that removes the limit, a ratio of 10 that persists
/// everything, a timeout that is already over).
/// </para>
/// <para>
/// Each case builds a real container from in-memory configuration (the path <c>AddTracon</c> takes) and runs the
/// startup validation the host runs, then reads the options. A feature with a switch is probed twice, with the
/// switch off and on, because many validators check a value only while its feature is on.
/// </para>
/// </remarks>
public sealed class ConfigurationPermutationTests
{
    private static readonly string[] CultureNames = ["tr-TR", "de-DE"];

    /// <summary>Texts a comma-decimal culture would read differently from the invariant one.</summary>
    private static readonly string[] CultureSensitiveTexts = ["0,5", "1,5", "1.5", "1.000,5", "1,000"];

    /// <summary>
    /// Scalars whose text is NOT read from configuration on purpose, each with the reason. The probe skips them.
    /// </summary>
    private static readonly Dictionary<string, string> NotBoundOnPurpose = new(StringComparer.Ordinal)
    {
        ["Tracon:Skills:Scripts:AllowStoredScripts"] = "a security boundary: configuration must not widen the executable surface",
    };

    /// <summary>
    /// Limits that accept <c>0</c> because it has a meaning, each with that meaning. A limit that is not here and
    /// accepts <c>0</c> stops the build: the operator who wrote it may have meant something else.
    /// </summary>
    private static readonly Dictionary<string, string> ZeroIsDocumented = new(StringComparer.Ordinal)
    {
        ["Tracon:AgentGraph:MaxDepth"] = "no sub-agent call is allowed",
        ["Tracon:AgentGraph:MaxDuration"] = "documented: zero removes the limit",
        ["Tracon:AgentGraph:MaxTotalCost"] = "documented: zero removes the limit",
        ["Tracon:AgentGraph:MaxTotalRuns"] = "documented: zero removes the limit",
        ["Tracon:AgentGraph:MaxTotalTokens"] = "documented: zero removes the limit",
        ["Tracon:Attachments:MaxBytes"] = "every attachment is over the limit (fail-closed)",
        ["Tracon:Idempotency:ReservationTimeout"] = "documented: zero never takes a reserved key over",
        ["Tracon:Knowledge:ChunkOverlap"] = "no overlap between chunks",
        ["Tracon:Observability:MaxJobLaneCardinality"] = "clamped to one series where it is read",
        ["Tracon:Observability:MaxSpansPerRun"] = "every span is dropped and a warning is logged (fail-closed, diagnostics only)",
        ["Tracon:Observability:SuccessSampleRatio"] = "persists no successful run",
        ["Tracon:OnlineEvaluation:LowScoreThreshold"] = "marks no score as low",
        ["Tracon:OnlineEvaluation:MaxScoresPerHour"] = "scores nothing (the cost cap is zero)",
        ["Tracon:OnlineEvaluation:SampleRate"] = "samples nothing",
        ["Tracon:Preflight:ReserveRatio"] = "reserves nothing for the answer",
        ["Tracon:RateLimit:PermitLimit"] = "documented: a value that is not positive keeps the one-permit meaning",
        ["Tracon:RateLimit:QueueLimit"] = "no waiting line",
        ["Tracon:RateLimit:Window"] = "documented: a value that is not positive keeps the one-minute meaning",
        ["Tracon:Retention:BatchDelay"] = "no pause between batches",
        ["Tracon:RunRecording:MaxPayloadLength"] = "zero is inside the validated range 0 to 1048576",
        ["Tracon:RunRecording:SinkTimeout"] = "documented: zero waits for the sink for ever",
        ["Tracon:Voice:Conversation:MaxConcurrentConnectionsPerTenant"] = "refuses every connection (fail-closed)",
        ["Tracon:Voice:Conversation:MaxSpokenCharactersPerTurn"] = "speaks nothing; the answer still streams as captions",
        ["Tracon:Voice:Conversation:MaxUtteranceBytes"] = "refuses every utterance (fail-closed)",
        ["Tracon:Voice:Live:MaxAppendsPerDelegation"] = "a delegation sends no append",
        ["Tracon:Voice:Live:MaxConcurrentDelegations"] = "refuses every delegation (fail-closed)",
        ["Tracon:Voice:Live:MaxConcurrentSessionsPerTenant"] = "refuses every session (fail-closed)",
        ["Tracon:Voice:Live:MaxLedgerEntriesPerDelegation"] = "a delegation carries no transcript",
        ["Tracon:Voice:Live:MaxTranscriptLedgerCharacters"] = "the ledger keeps nothing",
        ["Tracon:Webhooks:MaxExtraHeaders"] = "no extra header is allowed",
        ["Tracon:Webhooks:SignatureTolerance"] = "the recommended tolerance is none",
    };

    /// <summary>
    /// Limits that accept <c>-1</c> because it has a meaning, each with that meaning. A limit that is not here and
    /// accepts <c>-1</c> stops the build.
    /// </summary>
    private static readonly Dictionary<string, string> NegativeIsDocumented = new(StringComparer.Ordinal)
    {
        ["Tracon:AgentGraph:MaxDepth"] = "no sub-agent call is allowed (fail-closed)",
        ["Tracon:AgentGraph:MaxDuration"] = "documented: a negative value removes the limit",
        ["Tracon:AgentGraph:MaxTotalCost"] = "documented: a negative value removes the limit",
        ["Tracon:AgentGraph:MaxTotalRuns"] = "documented: a negative value removes the limit",
        ["Tracon:AgentGraph:MaxTotalTokens"] = "documented: a negative value removes the limit",
        ["Tracon:Attachments:MaxBytes"] = "every attachment is over the limit (fail-closed)",
        ["Tracon:Idempotency:ReservationTimeout"] = "documented: zero or less never takes a reserved key over",
        ["Tracon:Observability:MaxJobLaneCardinality"] = "clamped to one series where it is read",
        ["Tracon:Observability:MaxSpansPerRun"] = "every span is dropped and a warning is logged (fail-closed, diagnostics only)",
        ["Tracon:RateLimit:PermitLimit"] = "documented: a value that is not positive keeps the one-permit meaning",
        ["Tracon:RateLimit:QueueLimit"] = "clamped to zero where it is read",
        ["Tracon:RateLimit:Window"] = "documented: a value that is not positive keeps the one-minute meaning",
    };

    /// <summary>
    /// Text options that keep a whitespace-only value as written, where a number option treats it as not given.
    /// The behaviour is recorded here until it is decided; a key that starts to refuse the text stops the build,
    /// so the list only shrinks.
    /// </summary>
    private static readonly Dictionary<string, string> WhitespaceTextKept = new(StringComparer.Ordinal)
    {
        ["Tracon:ContentGuard:Pattern:MaskReplacement"] = "masks with blanks",
        ["Tracon:Knowledge:EmbeddingProvider"] = "names a provider called blanks; found at the first embedding call",
        ["Tracon:ContentProtection:ActiveKeyId"] = "refused while the feature is on, kept while it is off",
        ["Tracon:Images:Provider"] = "refused while the feature is on, kept while it is off",
        ["Tracon:Images:Model"] = "refused while the feature is on, kept while it is off",
        ["Tracon:Audit:ActorClaimType"] = "looks for a claim called blanks and finds none",
        ["Tracon:Audit:ChainKeyId"] = "stops the host through the hosted start check, not the options validation",
        ["Tracon:Pricing:Currency"] = "reports cost in a currency called blanks",
        ["Tracon:Voice:Conversation:VoiceId"] = "asks the provider for a voice called blanks",
        ["Tracon:Voice:Conversation:OutputMediaType"] = "asks for a media type called blanks",
        ["Tracon:Voice:Live:Instructions"] = "sends blanks as the instructions",
        ["Tracon:Webhooks:AllowedConfigurationPrefix"] = "allows no configuration key (fail-closed)",
        ["Tracon:SingletonExecution:OwnerId"] = "names the owner with blanks",
        ["Tracon:Mcp:AllowedConfigurationPrefix"] = "allows no configuration key (fail-closed)",
        ["Tracon:SessionOwnership:ManagementPolicy"] = "no policy is satisfied (fail-closed); the empty string is the documented way to say so",
    };

    private static readonly Lazy<IReadOnlyList<OptionScalar>> Scalars = new(OptionScalarCatalog.All);

    private static readonly ConcurrentDictionary<CaseKey, Outcome> Outcomes = new();

    [Fact]
    public void Every_text_either_binds_what_the_invariant_reading_says_or_stops_the_host_naming_the_key()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => !NotBoundOnPurpose.ContainsKey(scalar.Key)))
        {
            foreach (var enabled in Variants(scalar))
            {
                var baseline = Run(scalar, string.Empty, enabled, culture: null);

                if (baseline.Kind != OutcomeKind.Bound)
                {
                    // The switch alone does not start: nothing to learn from this variant.
                    continue;
                }

                foreach (var text in TextsFor(scalar))
                {
                    var outcome = Run(scalar, text, enabled, culture: null);
                    var describe = $"{scalar.Display} ({scalar.Key}) = {Show(text)}{(enabled ? " with its feature on" : string.Empty)}";

                    switch (outcome.Kind)
                    {
                        case OutcomeKind.Crash:
                            failures.Add($"{describe}: {outcome.Message}");
                            break;
                        case OutcomeKind.LateFailure:
                            failures.Add($"{describe}: the host started, and the first read of the options failed: {outcome.Message}");
                            break;
                        case OutcomeKind.StartFailure when !NamesTheKey(scalar, outcome.Message):
                            failures.Add($"{describe}: the host stopped, but the message names neither the key nor the member: {outcome.Message}");
                            break;
                        case OutcomeKind.Bound:
                            CheckBound(scalar, text, outcome, baseline, describe, failures);
                            break;
                    }
                }
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A text was read as something other than the invariant reading, or the pipeline failed in a way an operator cannot trace:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_reading_does_not_depend_on_the_culture_of_the_thread()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => scalar.IsReadScalar && !NotBoundOnPurpose.ContainsKey(scalar.Key)))
        {
            foreach (var text in CultureSensitiveTexts.Concat(TextsFor(scalar).Where(static text => text.Contains(':', StringComparison.Ordinal))))
            {
                var invariant = Run(scalar, text, enabled: false, culture: null);

                foreach (var name in CultureNames)
                {
                    var other = Run(scalar, text, enabled: false, name);

                    if (other.Kind != invariant.Kind || !string.Equals(other.ValueText, invariant.ValueText, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"{scalar.Key} = {Show(text)} under {name}: {other.Kind} {other.ValueText} " +
                            $"but under the invariant culture {invariant.Kind} {invariant.ValueText}.");
                    }
                }
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void A_comma_decimal_is_never_read_as_a_larger_number()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => scalar.ScalarType == typeof(double) || scalar.ScalarType == typeof(decimal) || scalar.ScalarType == typeof(float)))
        {
            foreach (var name in CultureNames)
            {
                var outcome = Run(scalar, "1,5", enabled: false, name);

                if (outcome.Kind == OutcomeKind.Bound)
                {
                    failures.Add($"{scalar.Key} = '1,5' under {name} bound {outcome.ValueText}: a comma is not a decimal separator here and must not read as 15 or 1.5.");
                }
            }
        }

        // A scan that found no number option would pass for the wrong reason.
        Scalars.Value.Count(static scalar => scalar.ScalarType == typeof(double) || scalar.ScalarType == typeof(decimal)).ShouldBeGreaterThan(3);
        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void A_limit_of_zero_or_less_is_refused_unless_its_meaning_is_documented()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => IsLimit(scalar)))
        {
            var enabled = FeatureOn(scalar);

            foreach (var (text, documented) in new[] { ("-1", NegativeIsDocumented), ("0", ZeroIsDocumented) })
            {
                if (documented.ContainsKey(scalar.Key))
                {
                    continue;
                }

                var outcome = Run(scalar, text, enabled, culture: null);

                if (outcome.Kind == OutcomeKind.Bound)
                {
                    failures.Add($"{scalar.Key} = {text}{(enabled ? " with its feature on" : string.Empty)} is accepted and binds {outcome.ValueText} (default {Format(scalar.Default)}).");
                }
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A limit of zero or less started the host. Refuse it in the options validator, or list it in {nameof(ZeroIsDocumented)} / {nameof(NegativeIsDocumented)} with the meaning it has:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_documented_meaning_of_zero_or_less_names_a_scalar_that_still_accepts_it()
    {
        var failures = new List<string>();

        foreach (var (documented, text) in new[] { (ZeroIsDocumented, "0"), (NegativeIsDocumented, "-1") })
        {
            foreach (var (key, reason) in documented)
            {
                var scalar = Scalars.Value.SingleOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));

                if (scalar is null)
                {
                    failures.Add($"'{key}' is listed with a meaning for {text}, but no scalar has that key.");

                    continue;
                }

                if (Run(scalar, text, FeatureOn(scalar), culture: null).Kind != OutcomeKind.Bound)
                {
                    failures.Add($"'{key}' ({reason}) refuses {text} now: remove it from the list.");
                }
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void No_number_option_takes_NaN_or_infinity()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => scalar.ScalarType == typeof(double) || scalar.ScalarType == typeof(float)))
        {
            var enabled = FeatureOn(scalar);

            foreach (var text in new[] { "NaN", "Infinity", "-Infinity", "1e309" })
            {
                var outcome = Run(scalar, text, enabled, culture: null);

                if (outcome.Kind == OutcomeKind.Bound)
                {
                    failures.Add($"{scalar.Key} = {text}{(enabled ? " with its feature on" : string.Empty)} binds {outcome.ValueText}: every comparison with it is false or true for ever.");
                }
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("Tracon:Observability:SuccessSampleRatio")]
    [InlineData("Tracon:Preflight:ReserveRatio")]
    [InlineData("Tracon:OnlineEvaluation:SampleRate")]
    public void A_ratio_outside_zero_to_one_stops_the_host(string key)
    {
        var scalar = Scalars.Value.Single(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));

        foreach (var enabled in Variants(scalar))
        {
            foreach (var text in new[] { "-0.5", "1.5", "10", "100" })
            {
                Run(scalar, text, enabled, culture: null).Kind.ShouldBe(
                    OutcomeKind.StartFailure,
                    $"{key} = {text}{(enabled ? " with its feature on" : string.Empty)}: a fraction between 0 and 1 was written as a percent or mistyped, and the setting looks applied.");
            }
        }
    }

    [Fact]
    public void Whitespace_text_is_kept_only_where_the_record_says_so()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => scalar.PropertyType == typeof(string)))
        {
            var kept = Variants(scalar)
                .Select(enabled => (enabled, Baseline: Run(scalar, string.Empty, enabled, culture: null), Outcome: Run(scalar, "   ", enabled, culture: null)))
                .Where(static run => run.Baseline.Kind == OutcomeKind.Bound)
                .Any(static run => run.Outcome.Kind == OutcomeKind.Bound);

            var listed = WhitespaceTextKept.ContainsKey(scalar.Key);

            if (kept && !listed)
            {
                failures.Add($"{scalar.Key} keeps a whitespace-only text as written and is not in {nameof(WhitespaceTextKept)}.");
            }

            if (!kept && listed)
            {
                failures.Add($"{scalar.Key} refuses a whitespace-only text now: remove it from {nameof(WhitespaceTextKept)}.");
            }
        }

        foreach (var key in WhitespaceTextKept.Keys.Where(static key => !Scalars.Value.Any(scalar => string.Equals(scalar.Key, key, StringComparison.Ordinal))))
        {
            failures.Add($"'{key}' is listed in {nameof(WhitespaceTextKept)}, but no scalar has that key.");
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    private static bool IsLimit(OptionScalar scalar)
    {
        if (scalar.ScalarType == typeof(bool) || scalar.ScalarType.IsEnum || scalar.ScalarType == typeof(string))
        {
            return false;
        }

        // A value a new instance holds that is itself zero or less is a switch (a limit that is off), not a limit.
        return scalar.Default is null || Convert.ToDouble(scalar.Default is TimeSpan span ? span.TotalSeconds : scalar.Default, CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Whether the scalar is probed with its feature switched on: it has a switch, and the switch alone starts the host.</summary>
    private static bool FeatureOn(OptionScalar scalar)
        => scalar.EnablingKeys().Count > 0 && Run(scalar, string.Empty, enabled: true, culture: null).Kind == OutcomeKind.Bound;

    private static IEnumerable<bool> Variants(OptionScalar scalar)
    {
        yield return false;

        if (scalar.EnablingKeys().Count > 0)
        {
            yield return true;
        }
    }

    private static IEnumerable<string> TextsFor(OptionScalar scalar)
    {
        var type = scalar.ScalarType;

        yield return string.Empty;
        yield return "   ";

        if (type == typeof(string))
        {
            yield break;
        }

        yield return "abc";
        yield return "-1";
        yield return "0";
        yield return "0,5";

        if (type == typeof(bool))
        {
            yield return "1";

            yield break;
        }

        if (type == typeof(long))
        {
            yield return "9223372036854775808";
        }
        else if (type == typeof(int) || type.IsEnum)
        {
            yield return "2147483648";
        }
        else if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            yield return "NaN";
            yield return "Infinity";
            yield return "1e309";
            yield return "1.5";
        }
        else if (type == typeof(TimeSpan))
        {
            yield return "1:00:00:00:00";
            yield return "30";
            yield return "24:00:00";
        }
    }

    private static void CheckBound(OptionScalar scalar, string text, Outcome outcome, Outcome baseline, string describe, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // Nothing or white space is "not given": the option keeps the value it has without the key.
            if (scalar.PropertyType != typeof(string)
                && !string.Equals(outcome.ValueText, baseline.ValueText, StringComparison.Ordinal))
            {
                failures.Add($"{describe}: not given, but the value changed from {baseline.ValueText} to {outcome.ValueText}.");
            }

            return;
        }

        if (scalar.PropertyType == typeof(string))
        {
            if (!string.Equals(outcome.ValueText, text, StringComparison.Ordinal))
            {
                failures.Add($"{describe}: bound {Show(outcome.ValueText)}, not the text as written.");
            }

            return;
        }

        var expected = InvariantReading(scalar.ScalarType, text);

        if (expected is null)
        {
            failures.Add($"{describe}: the invariant reading cannot read the text, but the option bound {outcome.ValueText}.");
        }
        else if (!string.Equals(expected, outcome.ValueText, StringComparison.Ordinal))
        {
            failures.Add($"{describe}: the invariant reading is {expected}, but the option bound {outcome.ValueText}.");
        }
    }

    /// <summary>The reading of a text by the rules the reader documents, written again here so the reader is not its own oracle.</summary>
    private static string? InvariantReading(Type type, string text)
    {
        var invariant = CultureInfo.InvariantCulture;

        if (type == typeof(int))
        {
            return int.TryParse(text, NumberStyles.Integer, invariant, out var value) ? Format(value) : null;
        }

        if (type == typeof(long))
        {
            return long.TryParse(text, NumberStyles.Integer, invariant, out var value) ? Format(value) : null;
        }

        if (type == typeof(double))
        {
            return double.TryParse(text, NumberStyles.Float, invariant, out var value) ? Format(value) : null;
        }

        if (type == typeof(float))
        {
            return float.TryParse(text, NumberStyles.Float, invariant, out var value) ? Format(value) : null;
        }

        if (type == typeof(decimal))
        {
            const NumberStyles Styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

            return decimal.TryParse(text, Styles, invariant, out var value) ? Format(value) : null;
        }

        if (type == typeof(bool))
        {
            return bool.TryParse(text, out var value) ? Format(value) : null;
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.TryParse(text, invariant, out var value) ? Format(value) : null;
        }

        if (type.IsEnum && Enum.TryParse(type, text, ignoreCase: true, out var member))
        {
            var flags = type.IsDefined(typeof(FlagsAttribute), inherit: false);

            return flags || (!text.Contains(',', StringComparison.Ordinal) && Enum.IsDefined(type, member!)) ? Format(member) : null;
        }

        return null;
    }

    private static bool NamesTheKey(OptionScalar scalar, string message)
        => message.Contains(scalar.Key, StringComparison.Ordinal)
           || message.Contains(scalar.Chain[^1].Name, StringComparison.Ordinal);

    private static string Format(object? value)
        => value switch
        {
            null => "null",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };

    private static string Show(string text) => text.Length == 0 ? "''" : $"'{text}'";

    private static Outcome Run(OptionScalar scalar, string text, bool enabled, string? culture)
        => Outcomes.GetOrAdd(new CaseKey(scalar.Key, text, enabled, culture), _ => Execute(scalar, text, enabled, culture));

    private static Outcome Execute(OptionScalar scalar, string text, bool enabled, string? culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;

        try
        {
            if (culture is not null)
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            }

            var values = new Dictionary<string, string?>(enabled ? scalar.EnablingKeys() : new Dictionary<string, string?>(StringComparer.Ordinal), StringComparer.Ordinal)
            {
                [scalar.Key] = text,
            };

            return Probe(scalar, values);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static Outcome Probe(OptionScalar scalar, Dictionary<string, string?> values)
    {
        ServiceProvider provider;

        try
        {
            provider = SectionBinding.Build(values, scalar.Compose);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new Outcome(OutcomeKind.Crash, string.Empty, $"building the container threw {exception.GetType().Name}: {exception.Message}");
        }

        using (provider)
        {
            try
            {
                provider.GetRequiredService<IStartupValidator>().Validate();
            }
            catch (OptionsValidationException exception)
            {
                return new Outcome(OutcomeKind.StartFailure, string.Empty, string.Join(" | ", exception.Failures));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new Outcome(OutcomeKind.Crash, string.Empty, $"the startup validation threw {exception.GetType().Name}, not an options validation failure: {exception.Message}");
            }

            var optionsType = typeof(IOptions<>).MakeGenericType(scalar.RootType);

            try
            {
                var root = optionsType.GetProperty(nameof(IOptions<object>.Value))!.GetValue(provider.GetRequiredService(optionsType))!;

                return new Outcome(OutcomeKind.Bound, Format(scalar.ReadFrom(root)), string.Empty);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is OptionsValidationException late)
            {
                return new Outcome(OutcomeKind.LateFailure, string.Empty, string.Join(" | ", late.Failures));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new Outcome(OutcomeKind.Crash, string.Empty, $"reading the options threw {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private enum OutcomeKind
    {
        Bound,
        StartFailure,
        LateFailure,
        Crash,
    }

    private sealed record Outcome(OutcomeKind Kind, string ValueText, string Message);

    private readonly record struct CaseKey(string Key, string Text, bool Enabled, string? Culture);
}
