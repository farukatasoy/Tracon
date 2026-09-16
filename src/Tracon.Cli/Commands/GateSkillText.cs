namespace Tracon.Cli.Commands;

/// <summary>
/// The one canonical gate procedure, and the shell that turns it into a skill
/// file a coding agent's harness actually loads.
/// </summary>
/// <remarks>
/// <para>
/// The procedure is held apart from the shell on purpose. The shell - front
/// matter, a revision marker, a file name - belongs to one harness and changes
/// with it. The procedure belongs to Tracon and must read the same whatever
/// wraps it, so a second harness is a second shell around this same text
/// rather than a second copy of it that drifts.
/// </para>
/// <para>
/// The text names files rather than repeating what is in them. A capability
/// list copied in here would be stale the moment the installed version moved,
/// and nothing would report it; the two files it points at are written against
/// the version actually installed on the machine.
/// </para>
/// </remarks>
internal static class GateSkillText
{
    /// <summary>The skill's name, which is also the directory that holds it.</summary>
    public const string SkillName = "tracon";

    /// <summary>
    /// The ceiling on the written file, in bytes. A gate skill is one screen of
    /// procedure, not a manual: every byte is spent out of the context budget
    /// of the agent that loads it, on every task that touches Tracon. The
    /// capability map it points at is more than twice this size, which is
    /// exactly why the map is pointed at rather than inlined.
    /// </summary>
    public const int MaximumBytes = 4096;

    /// <summary>
    /// Opening of the marker the build reads back. It carries the revision of
    /// the capability map the text was written from, which is the only thing
    /// about this file that can go out of date.
    /// </summary>
    /// <remarks>
    /// Not on the first line: a skill file begins with its front matter, and a
    /// harness that cannot parse the front matter does not load the skill at
    /// all. The marker follows it instead, and the analyzer searches the
    /// opening lines rather than only the first.
    /// </remarks>
    public const string MarkerOpening = "<!-- Tracon gate skill · revision: ";

    /// <summary>The revision of the capability map this build of the tool carries.</summary>
    /// <remarks>
    /// Computed rather than cached in a static initialiser: a missing resource
    /// then surfaces as its own exception instead of a type initialiser failure
    /// that also takes the text below with it.
    /// </remarks>
    public static string MapRevision => ReadMapRevision();

    /// <summary>
    /// The procedure itself, free of any harness's format. Shared by every
    /// shell, and compared against them by test.
    /// </summary>
    public static string Body { get; } =
        """
        # Tracon gate

        Read this before you write or change code that calls Tracon: `AddTracon`,
        `MapTracon`, an agent, a tool, a run, an evaluation, or a store.

        ## Do these in order

        1. Open `Tracon.LocalReference.md`, beside the project you are changing. It
           names the capability map and the XML documentation of the version
           installed on this machine, by absolute path.
        2. Read the capability map it names. That map lists what this version
           already ships and the call that turns each capability on.
        3. Find the exact member in the XML documentation before you call it. Every
           registration entry point carries a worked example there.
        4. Only then write the code.

        If `Tracon.LocalReference.md` is not beside the project, the build has not
        written it. Set the `TraconWriteAgentsFile` property to `true` in that
        project and build once.

        ## What this does not do

        This is a guardrail, not a boundary. It does not list the capabilities, and
        it cannot tell you that one is missing - only the map can. It checks
        nothing you write; the build does that, and reports what it finds as
        `TRC` warnings.

        Do not copy the map into this file. The map belongs to the installed
        version and moves when that version moves.
        """.ReplaceLineEndings("\n");

    /// <summary>
    /// The Claude Code shell: front matter first, then the marker, then the
    /// shared procedure.
    /// </summary>
    /// <remarks>
    /// The description is what the harness matches a task against, so it names
    /// the calls a task would mention rather than describing the file.
    /// </remarks>
    public static string ClaudeCodeSkill(string revision) =>
        $"""
        ---
        name: {SkillName}
        description: Read before writing or changing code that calls Tracon - AddTracon, MapTracon, an agent, a tool, a run, an evaluation, or a store. Points at the capability map and the API documentation of the installed version.
        ---

        {Marker(revision)}

        {Body}

        """.ReplaceLineEndings("\n");

    /// <summary>The full marker line for a revision.</summary>
    public static string Marker(string revision) =>
        $"{MarkerOpening}{revision} · written by `tracon agent-skill` -->";

    /// <summary>
    /// Reads the revision out of the capability map embedded in this tool.
    /// </summary>
    /// <remarks>
    /// Embedded rather than read from disk: the tool is installed separately
    /// from the packages, so there is no map beside it to read. What it stamps
    /// is therefore the revision this BUILD OF THE TOOL knows, and the build of
    /// the consuming project compares that against the revision its installed
    /// packages carry. When the two differ the answer is to update the tool,
    /// which is what the staleness diagnostic says.
    /// </remarks>
    private static string ReadMapRevision()
    {
        var assembly = typeof(GateSkillText).Assembly;
        using var stream = assembly.GetManifestResourceStream(MapResourceName)
            ?? throw new InvalidOperationException(
                $"The capability map is missing from {assembly.GetName().Name}. It is embedded by the project file.");

        using var reader = new StreamReader(stream);
        var first = reader.ReadLine() ?? string.Empty;

        const string Opening = "<!-- Tracon agent map · revision: ";
        var start = first.IndexOf(Opening, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException("The embedded capability map carries no revision marker.");
        }

        start += Opening.Length;
        var end = first.IndexOf(' ', start);

        return end > start
            ? first.Substring(start, end - start)
            : throw new InvalidOperationException("The embedded capability map's revision marker is malformed.");
    }

    /// <summary>Logical name of the embedded capability map; set by the project file.</summary>
    internal const string MapResourceName = "Tracon.Cli.Tracon.AgentMap.md";
}
