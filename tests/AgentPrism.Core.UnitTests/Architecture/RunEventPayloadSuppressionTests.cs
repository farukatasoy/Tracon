using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Keeps the <c>RecordToolPayloads</c> suppression rule stated in
/// <see cref="RunEventType"/>'s own documentation equal to the rule
/// <c>RunEventWriter</c> actually applies.
/// </summary>
/// <remarks>
/// <para>
/// <c>RunEventWriter</c> leaves <c>Payload</c> entirely <see langword="null"/>
/// when <c>AgentPrismRunRecordingOptions.RecordToolPayloads</c> is off, with a
/// short list of deliberate exceptions whose payload is the function itself,
/// not an observability detail. The enum documented that condition on three of
/// its roughly eighteen payload-claiming members and nowhere else, so a
/// consumer learned the rule member by member — and
/// <see cref="RunEventType.WorkflowRequest"/>, an exception, said nothing at
/// all, leaving a reader to apply the general rule and conclude wrongly that
/// its payload can vanish.
/// </para>
/// <para>
/// The fix states the rule once, in the enum's type-level remarks. This gate
/// is what keeps that single statement true: the exceptions named in the
/// prose must be exactly the exceptions the writer grants. A fourth exception
/// added to the writer fails here until the prose names it, and an exception
/// removed from the writer fails until the prose drops it.
/// </para>
/// <para>
/// This is the machine-checkable half of the problem. The other half — does
/// each member's prose describe the keys the writer actually emits — is the
/// documented limit of <see cref="RunEventPayloadContractTests"/> and stays
/// there.
/// </para>
/// </remarks>
public sealed class RunEventPayloadSuppressionTests
{
    private const string Option = "RecordToolPayloads";

    /// <summary>
    /// The writer's conditional, from <c>Payload =</c> to the <c>?</c> that
    /// ends the condition. Every <c>RunEventType.X</c> inside it is an event
    /// whose payload survives with the option off.
    /// </summary>
    private static readonly Regex WriterCondition = new(
        @"Payload\s*=\s*_options\." + Option + @"(?<exceptions>.*?)\?\s*Truncate",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The enum's type-level remarks: everything before the <c>[JsonConverter]</c>
    /// attribute that closes the type's own documentation.
    /// </summary>
    private static readonly Regex TypeLevelDocumentation = new(
        @"\A(?<doc>.*?)^\[JsonConverter",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex EventTypeReference = new(
        @"(?:RunEventType\.|see cref=""(?:RunEventType\.)?)(?<name>[A-Z][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void The_enum_documents_the_suppression_rule_once_at_type_level()
    {
        var documentation = ReadTypeLevelDocumentation();

        documentation.ShouldContain(
            Option,
            customMessage:
            $"RunEventType's type-level remarks no longer mention {Option}. The rule that every " +
            "event's Payload is suppressed with that option off is stated there and nowhere else; " +
            "without it a consumer has to read RunEventWriter to learn it.");
    }

    [Fact]
    public void The_documented_exceptions_are_exactly_the_ones_the_writer_grants()
    {
        var writer = ReadWriterExceptions();
        var documented = ReadDocumentedExceptions();

        writer.ShouldNotBeEmpty(
            "The scan found no exception in RunEventWriter's Payload condition — the writer moved " +
            "or its shape changed, and this gate is measuring nothing.");

        var undocumented = writer.Except(documented, StringComparer.Ordinal).ToList();
        var stale = documented.Except(writer, StringComparer.Ordinal).ToList();

        undocumented.ShouldBeEmpty(
            $"RunEventWriter writes these events' payloads even with {Option} off, but RunEventType's " +
            $"type-level remarks do not name them: {string.Join(", ", undocumented)}. Name them there, " +
            "with the reason their payload is the function rather than an observability detail.");

        stale.ShouldBeEmpty(
            $"RunEventType's remarks name these as exempt from {Option}, but RunEventWriter no longer " +
            $"exempts them: {string.Join(", ", stale)}. Their payload now disappears with the option " +
            "off and the shipped documentation says it does not.");
    }

    [Fact]
    public void No_member_repeats_the_rule_the_type_level_remarks_already_state()
    {
        // Three members carried the condition and fifteen did not, which is
        // how a reader ended up believing it applied only to those three.
        // One statement or none: a member may only mention the option to
        // explain its OWN exemption.
        var source = File.ReadAllText(RunEventTypePath);
        var members = source[ReadTypeLevelDocumentation().Length..];
        var documented = ReadDocumentedExceptions();
        var offenders = new List<string>();

        foreach (var block in members.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!block.Contains(Option, StringComparison.Ordinal))
            {
                continue;
            }

            var member = MemberNameOf(block);

            if (member is not null && !documented.Contains(member))
            {
                offenders.Add(member);
            }
        }

        offenders.ShouldBeEmpty(
            $"These members restate the {Option} rule that RunEventType's type-level remarks already " +
            $"state for every member: {string.Join(", ", offenders)}. Repeating it is how the rule " +
            "came to look member-specific. Only an EXEMPT member may mention the option, to explain " +
            "its own exemption.");
    }

    private static HashSet<string> ReadWriterExceptions()
    {
        var match = WriterCondition.Match(File.ReadAllText(RunEventWriterPath));

        match.Success.ShouldBeTrue(
            $"'{RunEventWriterPath}' no longer carries a 'Payload = _options.{Option} ... ? Truncate' " +
            "conditional. The writer was restructured; re-point this gate at the new shape.");

        return [.. EventTypeReference.Matches(match.Groups["exceptions"].Value).Select(m => m.Groups["name"].Value)];
    }

    private static HashSet<string> ReadDocumentedExceptions()
    {
        var documentation = ReadTypeLevelDocumentation();
        var start = documentation.IndexOf(Option, StringComparison.Ordinal);

        if (start < 0)
        {
            return [];
        }

        return [.. EventTypeReference.Matches(documentation[start..]).Select(m => m.Groups["name"].Value)];
    }

    private static string ReadTypeLevelDocumentation()
    {
        var match = TypeLevelDocumentation.Match(File.ReadAllText(RunEventTypePath));

        match.Success.ShouldBeTrue(
            $"'{RunEventTypePath}' no longer opens with documentation followed by [JsonConverter].");

        return match.Groups["doc"].Value;
    }

    private static string? MemberNameOf(string block)
    {
        var match = Regex.Match(
            block,
            @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\d+\s*,",
            RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string RunEventTypePath { get; } = Path.Combine(
        RepositoryRoot, "src", "AgentPrism.Abstractions", "Runs", "RunEventType.cs");

    private static string RunEventWriterPath { get; } = Path.Combine(
        RepositoryRoot, "src", "AgentPrism.Core", "Recording", "RunEventWriter.cs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
