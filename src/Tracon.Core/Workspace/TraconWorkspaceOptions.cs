namespace Tracon;

/// <summary>
/// Limits of the session workspace (<see cref="WorkspaceSettings"/>).
/// </summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:Workspace</c> configuration section. A write that would pass a limit
/// is refused, and the agent reads "workspace quota reached" as the result of the tool call; the
/// run goes on. The limits are measured against the files as the store keeps them: a SQL Server
/// store keeps text as UTF-16, so it reaches a limit with half as many ASCII characters as another
/// store does.
/// </para>
/// <para>
/// A file counts its content and also a fixed 1 KiB for its row in the limit of a session and of a tenant, so a model
/// that writes thousands of empty files reaches the limit instead of filling the table. The limits are soft: two writes
/// that run at the same moment can pass a limit together by the size of one file; the next write is refused.
/// </para>
/// </remarks>
/// <example>
/// <code language="json">
/// "Tracon": {
///   "Workspace": { "MaxFileBytes": 524288, "MaxSessionBytes": 10485760 }
/// }
/// </code>
/// </example>
public sealed class TraconWorkspaceOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:Workspace";

    /// <summary>
    /// Gets or sets the most bytes (UTF-8) one file may hold. Defaults to 256 KiB.
    /// </summary>
    public long MaxFileBytes { get; set; } = 256 * 1024;

    /// <summary>
    /// Gets or sets the most bytes all the files of one session may hold. Defaults to 5 MiB.
    /// </summary>
    public long MaxSessionBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most bytes all the workspaces of one tenant may hold. Defaults to 500 MiB.
    /// </summary>
    public long MaxTenantBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>
    /// Gets or sets how many bytes each file counts beyond its content, for the limit of a session and of a tenant. A file
    /// is a row with a path, a tenant, an agent and two timestamps, and an empty file would otherwise cost nothing.
    /// Internal: a host does not tune it, and a test sets it to zero to count bytes alone.
    /// </summary>
    internal long FileOverheadBytes { get; set; } = 1024;
}
