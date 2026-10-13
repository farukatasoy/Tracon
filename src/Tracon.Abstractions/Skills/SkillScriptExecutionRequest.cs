using System.Globalization;
using System.Text;

namespace Tracon;

/// <summary>
/// One skill script that passed every execution gate, as handed to an
/// <see cref="ISkillScriptExecutor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The request carries everything a remote runner needs: the content, its hash,
/// the arguments, and the limits. It carries no environment value: which host
/// variables a local process inherits is a setting of the built-in executor, and
/// no secret travels in the request.
/// </para>
/// <para>
/// The script sees its arguments as JSON on standard input, and two environment
/// variables: <c>TRACON_SKILL_NAME</c> (the skill name) and <c>TRACON_SKILL_TEMP</c>
/// (a writable directory that belongs to this one call). An executor provides the
/// same to keep scripts portable between executors.
/// </para>
/// <para>
/// <see cref="object.ToString"/> leaves out <see cref="Content"/> and
/// <see cref="ArgumentsJson"/>, so logging a request does not write the script or
/// what the model passed to it.
/// </para>
/// </remarks>
public sealed record SkillScriptExecutionRequest
{
    /// <summary>The tenant whose grant opened the gates.</summary>
    public required string TenantId { get; init; }

    /// <summary>The run that called the script, or <see langword="null"/> outside a run.</summary>
    public Guid? RunId { get; init; }

    /// <summary>The name of the skill that carries the script.</summary>
    public required string SkillName { get; init; }

    /// <summary>The script name within the skill.</summary>
    /// <remarks>
    /// For a <see cref="SkillScriptOrigin.Stored"/> script, the name the skill
    /// stores; Tracon checks that it is a plain file name (no directory separator,
    /// path root, or <c>..</c>) before the call. For a
    /// <see cref="SkillScriptOrigin.File"/> script, the file's path relative to the
    /// skill directory, with its extension (for example <c>scripts/fetch.py</c>).
    /// Do not use it as a file name without checking it.
    /// </remarks>
    public required string ScriptName { get; init; }

    /// <summary>Where the content came from.</summary>
    public required SkillScriptOrigin Origin { get; init; }

    /// <summary>The file extension, without the dot (for example <c>py</c>).</summary>
    public required string Extension { get; init; }

    /// <summary>
    /// The interpreter-allow-list value for <see cref="Extension"/>.
    /// </summary>
    /// <remarks>
    /// The built-in executor starts this value as the interpreter's path. A remote
    /// executor can read it as a runtime name instead (for example <c>python-3.12</c>).
    /// Only values from the host's own allow-list reach an executor.
    /// </remarks>
    public required string Interpreter { get; init; }

    /// <summary>The script's source text.</summary>
    /// <remarks>
    /// For a <see cref="SkillScriptOrigin.Stored"/> script, the exact content the
    /// grant pins. For a <see cref="SkillScriptOrigin.File"/> script, the file's
    /// text as Tracon read it, decoded as UTF-8, after the quota was taken. Other
    /// files next to it are not carried.
    /// </remarks>
    public required string Content { get; init; }

    /// <summary>
    /// The SHA-256 of the UTF-8 bytes of <see cref="Content"/>, as 64 upper-case
    /// hexadecimal characters.
    /// </summary>
    /// <remarks>
    /// A remote runner can recompute it to confirm that the content arrived
    /// unchanged. It is not the grant's pin value: the pin also covers the
    /// extension and the argument schema.
    /// </remarks>
    public required string ContentSha256 { get; init; }

    /// <summary>
    /// The full path of the script file on the host, for a
    /// <see cref="SkillScriptOrigin.File"/> script; <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>
    /// The built-in executor runs the file in place, so the script can use files
    /// next to it. A remote executor cannot read this path; it runs
    /// <see cref="Content"/>.
    /// </remarks>
    public string? SourcePath { get; init; }

    /// <summary>
    /// The arguments the model passed, as JSON, written to the script's standard
    /// input. <see langword="null"/> when the model passed none; the script then
    /// reads an empty, closed standard input.
    /// </summary>
    public string? ArgumentsJson { get; init; }

    /// <summary>The longest time the script may run.</summary>
    /// <remarks>
    /// Stop the script when it passes this time and report
    /// <see cref="SkillScriptExecutionResult.TimedOut"/>. Tracon waits this long plus
    /// a fixed grace period, then cancels the token, stops waiting, and records a
    /// timeout itself.
    /// </remarks>
    public required TimeSpan Timeout { get; init; }

    /// <summary>The largest number of UTF-8 bytes kept from each output stream.</summary>
    /// <remarks>
    /// Standard output and standard error are capped separately. Tracon applies the
    /// cap again to the result and adds the truncation notice itself.
    /// </remarks>
    public required int MaxOutputBytes { get; init; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"TenantId = {TenantId}, RunId = {RunId}, ");
        builder.Append(CultureInfo.InvariantCulture, $"SkillName = {SkillName}, ScriptName = {ScriptName}, ");
        builder.Append(CultureInfo.InvariantCulture, $"Origin = {Origin}, Extension = {Extension}, Interpreter = {Interpreter}, ");
        builder.Append(CultureInfo.InvariantCulture, $"ContentSha256 = {ContentSha256}, SourcePath = {SourcePath}, ");
        builder.Append(CultureInfo.InvariantCulture, $"Timeout = {Timeout}, MaxOutputBytes = {MaxOutputBytes}");
        return true;
    }
}
