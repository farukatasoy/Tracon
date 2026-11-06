using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
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
public sealed partial class ConfigurationPermutationTests
{
    private static readonly string[] CultureNames = ["tr-TR", "de-DE"];

    /// <summary>Texts a comma-decimal culture would read differently from the invariant one, by the type that reads them.</summary>
    private static readonly string[] WholeNumberCultureTexts = ["0,5", "1,000"];

    private static readonly string[] FractionCultureTexts = ["0,5", "1.5", "1.000,5", "1,000"];

    private static readonly string[] TimeSpanCultureTexts = ["0,5", "00:00:30,5", "00:00:30.5"];

    private static readonly Lazy<IReadOnlyList<OptionScalar>> Scalars = new(OptionScalarCatalog.All);

    private static readonly ConcurrentDictionary<CaseKey, Outcome> Outcomes = new();

    [Fact]
    public void Every_text_either_binds_what_the_invariant_reading_says_or_stops_the_host_naming_the_key()
    {
        Warm(
            from scalar in Scalars.Value
            where !SkipsReadingProbe(scalar)
            from enabled in Variants(scalar)
            from text in TextsFor(scalar).Prepend(string.Empty)
            select new CaseKey(scalar.Key, text, enabled, Culture: null));

        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => !SkipsReadingProbe(scalar)))
        {
            foreach (var enabled in Variants(scalar))
            {
                var baseline = Run(scalar, string.Empty, enabled, culture: null);

                if (baseline.Kind == OutcomeKind.Crash)
                {
                    // A raw exception with the key left empty is not "the switch alone does not start": the key is
                    // "not given" and the host has to start, or stop with an options failure. A reader that throws
                    // for an empty text (the framework binder does, for a number that is not nullable) would hide
                    // behind the skip below, and every text of the key with it.
                    failures.Add($"{scalar.Display} ({scalar.Key}) = ''{(enabled ? " with its feature on" : string.Empty)}: {baseline.Message}");

                    continue;
                }

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
        Warm(
            from scalar in Scalars.Value
            where !SkipsReadingProbe(scalar)
            from text in CultureTextsFor(scalar)
            from culture in CultureNames.Prepend(null)
            select new CaseKey(scalar.Key, text, Enabled: false, culture));

        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => !SkipsReadingProbe(scalar)))
        {
            foreach (var text in CultureTextsFor(scalar))
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
        Scalars.Value.Count(static scalar => scalar.ScalarType == typeof(double) || scalar.ScalarType == typeof(decimal)).ShouldBeGreaterThanOrEqualTo(MinimumNumberScalars);
        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void A_limit_of_zero_or_less_is_refused_unless_its_meaning_is_documented()
    {
        var failures = new List<string>();

        foreach (var scalar in Scalars.Value.Where(static scalar => IsLimit(scalar)))
        {
            var enabled = FeatureOn(scalar);

            foreach (var (text, documented) in new[] { (NegativeText(scalar), NegativeIsDocumented), (ZeroText(scalar), ZeroIsDocumented) })
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

        foreach (var (documented, isZero) in new[] { (ZeroIsDocumented, true), (NegativeIsDocumented, false) })
        {
            foreach (var (key, reason) in documented)
            {
                var scalar = Scalars.Value.SingleOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));

                if (scalar is null)
                {
                    failures.Add($"'{key}' is listed with a meaning for {(isZero ? "zero" : "a negative value")}, but no scalar has that key.");

                    continue;
                }

                var text = isZero ? ZeroText(scalar) : NegativeText(scalar);

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

    [Fact]
    public void A_ratio_outside_zero_to_one_stops_the_host()
    {
        foreach (var key in RatioKeys)
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
    }

    [Fact]
    public void A_text_that_is_only_white_space_stops_the_host_naming_the_key()
    {
        var failures = new List<string>();
        var probed = 0;

        foreach (var scalar in Scalars.Value.Where(static scalar => scalar.PropertyType == typeof(string)))
        {
            var kept = false;

            foreach (var enabled in Variants(scalar))
            {
                // The variant has to start with the key left out, or there is nothing to compare with.
                if (Run(scalar, string.Empty, enabled, culture: null).Kind != OutcomeKind.Bound)
                {
                    continue;
                }

                probed++;

                var outcome = Run(scalar, "   ", enabled, culture: null);
                var describe = $"{scalar.Display} ({scalar.Key}) = '   '{(enabled ? " with its feature on" : string.Empty)}";

                if (outcome.Kind == OutcomeKind.Bound)
                {
                    kept = true;

                    if (!WhitespaceTextKept.ContainsKey(scalar.Key))
                    {
                        failures.Add($"{describe} is kept as written ({Show(outcome.ValueText)}): a text of blanks is a typing mistake, and the setting looks applied.");
                    }
                }
                else if (outcome.Kind != OutcomeKind.StartFailure || !NamesTheKey(scalar, outcome.Message))
                {
                    failures.Add($"{describe}: {outcome.Kind}, and the message does not name the key or the member: {outcome.Message}");
                }
            }

            if (!kept && WhitespaceTextKept.ContainsKey(scalar.Key))
            {
                failures.Add($"{scalar.Key} refuses a whitespace-only text now: remove it from {nameof(WhitespaceTextKept)}.");
            }
        }

        foreach (var key in WhitespaceTextKept.Keys.Where(static key => !Scalars.Value.Any(scalar => string.Equals(scalar.Key, key, StringComparison.Ordinal))))
        {
            failures.Add($"'{key}' is listed in {nameof(WhitespaceTextKept)}, but no scalar has that key.");
        }

        // A scan that found few text options would pass for the wrong reason.
        probed.ShouldBeGreaterThanOrEqualTo(MinimumTextScalars);
        failures.ShouldBeEmpty(
            customMessage: $"A text option kept a value made only of white space. Refuse it in the options validator, or record it in {nameof(WhitespaceTextKept)} until it is decided:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    private static bool SkipsReadingProbe(OptionScalar scalar)
        => NotBoundOnPurpose.ContainsKey(scalar.Key);

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

    /// <summary>The texts whose reading could depend on the culture: a number or a time span, not a switch, a name or text.</summary>
    private static string[] CultureTextsFor(OptionScalar scalar)
        => scalar.ScalarType == typeof(int) || scalar.ScalarType == typeof(long)
            ? WholeNumberCultureTexts
            : scalar.ScalarType == typeof(double) || scalar.ScalarType == typeof(float) || scalar.ScalarType == typeof(decimal)
                ? FractionCultureTexts
                : scalar.ScalarType == typeof(TimeSpan) ? TimeSpanCultureTexts : [];

    /// <summary>Zero as the scalar writes it: a time span is a clock, where a bare <c>0</c> is not readable.</summary>
    private static string ZeroText(OptionScalar scalar) => scalar.ScalarType == typeof(TimeSpan) ? "00:00:00" : "0";

    /// <summary>A negative value as the scalar writes it.</summary>
    private static string NegativeText(OptionScalar scalar) => scalar.ScalarType == typeof(TimeSpan) ? "-00:00:01" : "-1";

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
            yield return "00:00:00";
            yield return "00:00:30";
            yield return "0:00:30";
            yield return "1.00:00:00";
            yield return "-00:00:01";
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
            // The documented shape is the clock: a bare number is not days and hours do not pass 23, which
            // TimeSpan.TryParse alone would read as days. Checked by pattern here so the reader is not its own oracle.
            var clock = text.Trim();

            return TimeSpanShape().IsMatch(clock) && TimeSpan.TryParse(clock, invariant, out var value) ? Format(value) : null;
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

    [GeneratedRegex("^-?(?:[0-9]{1,8}\\.)?(?:[01]?[0-9]|2[0-3]):[0-5]?[0-9]:[0-5]?[0-9](?:\\.[0-9]{1,7})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimeSpanShape();

    private static string Show(string text) => text.Length == 0 ? "''" : $"'{text}'";

    /// <summary>Runs the cases on all cores and keeps their outcomes, so that the loops of a test only read them.</summary>
    private static void Warm(IEnumerable<CaseKey> cases)
    {
        var byKey = Scalars.Value.ToDictionary(static scalar => scalar.Key, StringComparer.Ordinal);

        Parallel.ForEach(
            cases,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) },
            key => Run(byKey[key.Key], key.Text, key.Enabled, key.Culture));
    }

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

            // What the section needs to start at all, then what switches the feature on, then the text under test.
            var values = new Dictionary<string, string?>(scalar.BaseKeys, StringComparer.Ordinal);

            if (enabled)
            {
                foreach (var (key, value) in scalar.EnablingKeys())
                {
                    values[key] = value;
                }
            }

            values[scalar.Key] = text;

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
