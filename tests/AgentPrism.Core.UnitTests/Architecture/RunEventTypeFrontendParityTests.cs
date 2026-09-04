using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Keeps <c>RunEventType</c> in sync with its hand-maintained TypeScript twin,
/// <c>src/AgentPrism.UI/frontend/src/lib/run-event.ts</c> (phase 141).
/// </summary>
/// <remarks>
/// <para>
/// An SSE frame carries no OpenAPI schema, so that union type cannot be
/// generated the way the rest of <c>@agentprism/client</c> is — the file's own
/// doc comment says it is "kept in sync by hand". A member added on the C#
/// side and forgotten on the TypeScript side compiles clean on both ends:
/// the backend happily emits the new type name over the wire, and the console
/// falls back to a generic, unstyled row for it (K-411 class drift).
/// </para>
/// <para>
/// This gate is a plain set-equality check, not a ratchet: unlike
/// <see cref="RunEventPayloadContractTests"/>, there is no legitimate reason
/// for the two sides to differ even temporarily, so nothing here is allowed
/// to stay "uncovered".
/// </para>
/// </remarks>
public sealed class RunEventTypeFrontendParityTests
{
    private static readonly Regex CSharpEnumMember = new(
        @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\d+\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex TypeScriptUnionMember = new(
        @"^\s*\|\s*'(?<name>[A-Za-z_][A-Za-z0-9_]*)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void The_TypeScript_union_names_exactly_the_same_members_as_the_C_Sharp_enum()
    {
        var backend = ReadCSharpMembers();
        var frontend = ReadTypeScriptMembers();

        backend.ShouldNotBeEmpty("The scan found no C# enum member at all -- RunEventType.cs moved or its shape changed.");
        frontend.ShouldNotBeEmpty("The scan found no TypeScript union member at all -- run-event.ts moved or its shape changed.");

        var missingFromFrontend = backend.Except(frontend, StringComparer.Ordinal).ToList();
        var missingFromBackend = frontend.Except(backend, StringComparer.Ordinal).ToList();

        missingFromFrontend.ShouldBeEmpty(
            $"run-event.ts's RunEventType union is missing: {string.Join(", ", missingFromFrontend)}. " +
            "Add the member there (and to run-detail.tsx's EVENT_STYLE, which TypeScript then forces to be exhaustive).");

        missingFromBackend.ShouldBeEmpty(
            $"run-event.ts's RunEventType union names members RunEventType.cs does not have: {string.Join(", ", missingFromBackend)}. " +
            "One side renamed or removed a member without the other.");
    }

    private static HashSet<string> ReadCSharpMembers()
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(CSharpPath))
        {
            var match = CSharpEnumMember.Match(line);

            if (match.Success)
            {
                members.Add(match.Groups["name"].Value);
            }
        }

        return members;
    }

    private static HashSet<string> ReadTypeScriptMembers()
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(TypeScriptPath))
        {
            var match = TypeScriptUnionMember.Match(line);

            if (match.Success)
            {
                members.Add(match.Groups["name"].Value);
            }
        }

        return members;
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string CSharpPath { get; } = Path.Combine(
        RepositoryRoot, "src", "AgentPrism.Abstractions", "Runs", "RunEventType.cs");

    private static string TypeScriptPath { get; } = Path.Combine(
        RepositoryRoot, "src", "AgentPrism.UI", "frontend", "src", "lib", "run-event.ts");

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
