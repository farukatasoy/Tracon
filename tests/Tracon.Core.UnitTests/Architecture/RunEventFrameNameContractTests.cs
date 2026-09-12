using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Keeps the recorded event stream's SSE frame names (<c>RunEndpoints.EventName</c>)
/// complete and in sync with the console's <c>EVENT_STYLE</c> table (phase 145).
/// </summary>
/// <remarks>
/// <para>
/// Before this phase, 21 of 31 <c>RunEventType</c> members fell through to the
/// <c>"unknown"</c> branch even though the method's own doc comment calls the
/// mapping a "stable contract" and the console already carried a label for
/// every member (<c>run-detail.tsx</c>'s <c>EVENT_STYLE</c>, which TypeScript's
/// <c>Record&lt;RunEventType, ...&gt;</c> already forces to be exhaustive).
/// </para>
/// <para>
/// This is a plain set-equality/completeness check, not a ratchet
/// (<see cref="RunEventPayloadContractTests"/>'s pattern): there is no
/// legitimate reason for a member to stay unnamed, so nothing here is allowed
/// to stay "uncovered".
/// </para>
/// </remarks>
public sealed class RunEventFrameNameContractTests
{
    /// <summary>
    /// The ten frame names shipped before this phase. These strings are FIXED —
    /// a real client already depends on them; changing one is a breaking
    /// change this phase must not make.
    /// </summary>
    private static readonly Dictionary<string, string> ShippedFrameNames = new(StringComparer.Ordinal)
    {
        ["RunStarted"] = "run.started",
        ["MessageDelta"] = "message.delta",
        ["MessageCompleted"] = "message.completed",
        ["ToolInvoking"] = "tool.invoking",
        ["ToolInvoked"] = "tool.invoked",
        ["ToolFailed"] = "tool.failed",
        ["RunCompleted"] = "run.completed",
        ["RunFailed"] = "run.failed",
        ["ChildRunStarted"] = "child.started",
        ["ChildRunCompleted"] = "child.completed",
    };

    private static readonly Regex CSharpEnumMember = new(
        @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\d+\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    // Matches "RunEventType.<Name> => "<frame-name>"," in RunEndpoints.EventName,
    // and the catch-all "_ => "unknown",". A K-642-class scan: this does NOT
    // assume the arm is on one line beyond what the source actually is today,
    // but each arm here IS single-line by construction (a plain switch
    // expression), so a regex that requires the whole arm on one line is safe.
    private static readonly Regex ServerFrameNameArm = new(
        """^\s*RunEventType\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=>\s*"(?<frame>[^"]*)"\s*,\s*$""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ServerCatchAllArm = new(
        """^\s*_\s*=>\s*"unknown"\s*,\s*$""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex FrontendStyleEntry = new(
        """^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*):\s*\{\s*label:\s*'(?<frame>[^']*)'""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_RunEventType_member_has_a_named_frame_none_falls_to_unknown()
    {
        var members = ReadCSharpEnumMembers();
        var serverNames = ReadServerFrameNames();

        members.ShouldNotBeEmpty("The scan found no RunEventType member at all -- RunEventType.cs moved or its shape changed.");
        serverNames.ShouldNotBeEmpty("The scan found no EventName switch arm at all -- RunEndpoints.cs moved or its shape changed.");

        var unnamed = members.Except(serverNames.Keys, StringComparer.Ordinal).ToList();

        unnamed.ShouldBeEmpty(
            $"RunEndpoints.EventName has no named frame for: {string.Join(", ", unnamed)}. " +
            "These members fall through to \"unknown\" on the wire even though the recorded " +
            "event stream's doc comment calls the mapping a stable contract.");

        var stray = serverNames.Keys.Except(members, StringComparer.Ordinal).ToList();

        stray.ShouldBeEmpty(
            $"RunEndpoints.EventName names a member RunEventType.cs does not have: {string.Join(", ", stray)}.");

        serverNames.Values.Contains("unknown", StringComparer.Ordinal).ShouldBeFalse(
            "A member's own arm resolves to the literal \"unknown\" -- pick a real frame name.");
    }

    [Fact]
    public void The_unknown_catch_all_branch_is_still_present()
    {
        // 145.2: the catch-all is NOT deleted -- a future RunEventType member
        // must still compile against this switch. The completeness guarantee
        // comes from the test above (no member's OWN arm may resolve to
        // "unknown"), not from removing the fallback.
        var source = File.ReadAllLines(ServerPath);

        source.Any(static line => ServerCatchAllArm.IsMatch(line)).ShouldBeTrue(
            "RunEndpoints.EventName's \"_ => \\\"unknown\\\"\" catch-all is missing. " +
            "It must stay so the switch keeps compiling against a future RunEventType member.");
    }

    [Fact]
    public void Shipped_frame_names_are_unchanged()
    {
        var serverNames = ReadServerFrameNames();

        foreach (var (member, expectedFrame) in ShippedFrameNames)
        {
            serverNames.ShouldContainKeyAndValue(member, expectedFrame);
        }
    }

    [Fact]
    public void The_server_frame_name_matches_the_consoles_EVENT_STYLE_label_for_every_member()
    {
        var serverNames = ReadServerFrameNames();
        var frontendLabels = ReadFrontendStyleLabels();

        serverNames.ShouldNotBeEmpty("The scan found no EventName switch arm at all -- RunEndpoints.cs moved or its shape changed.");
        frontendLabels.ShouldNotBeEmpty("The scan found no EVENT_STYLE entry at all -- run-detail.tsx moved or its shape changed.");

        var mismatched = new List<string>();

        foreach (var (member, serverFrame) in serverNames)
        {
            if (!frontendLabels.TryGetValue(member, out var frontendLabel))
            {
                mismatched.Add($"{member}: console EVENT_STYLE has no entry");

                continue;
            }

            if (!string.Equals(serverFrame, frontendLabel, StringComparison.Ordinal))
            {
                mismatched.Add($"{member}: server='{serverFrame}' console='{frontendLabel}'");
            }
        }

        mismatched.ShouldBeEmpty(
            $"Server frame name and console EVENT_STYLE label disagree: {string.Join("; ", mismatched)}.");
    }

    private static HashSet<string> ReadCSharpEnumMembers()
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(RunEventTypePath))
        {
            var match = CSharpEnumMember.Match(line);

            if (match.Success)
            {
                members.Add(match.Groups["name"].Value);
            }
        }

        return members;
    }

    private static Dictionary<string, string> ReadServerFrameNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(ServerPath))
        {
            var match = ServerFrameNameArm.Match(line);

            if (match.Success)
            {
                names[match.Groups["name"].Value] = match.Groups["frame"].Value;
            }
        }

        return names;
    }

    private static Dictionary<string, string> ReadFrontendStyleLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(FrontendStylePath))
        {
            var match = FrontendStyleEntry.Match(line);

            if (match.Success)
            {
                labels[match.Groups["name"].Value] = match.Groups["frame"].Value;
            }
        }

        return labels;
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string RunEventTypePath { get; } = Path.Combine(
        RepositoryRoot, "src", "Tracon.Abstractions", "Runs", "RunEventType.cs");

    private static string ServerPath { get; } = Path.Combine(
        RepositoryRoot, "src", "Tracon.AspNetCore", "Endpoints", "RunEndpoints.cs");

    private static string FrontendStylePath { get; } = Path.Combine(
        RepositoryRoot, "src", "Tracon.UI", "frontend", "src", "screens", "run-detail.tsx");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
